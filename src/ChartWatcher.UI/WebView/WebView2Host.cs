using System.Text.Json;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using ChartWatcher.Application.Services;
using ChartWatcher.Core.Sources;
using Serilog;

namespace ChartWatcher.UI.WebView;

/// <summary>
/// Wraps a single WebView2 control for one Source.
/// Handles environment creation (isolated UserDataFolder), navigation,
/// agent injection, and message routing via the bridge protocol.
/// </summary>
public sealed class WebView2Host : IAsyncDisposable
{
    private static readonly JsonSerializerOptions _jsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly Source _source;
    private readonly ILogger _log;
    private readonly string _userDataFolder;
    private WebView2 _webView = null!;
    private bool _initialized;
    private bool _agentInjected;
    private string? _cachedAgentJs;

    public event EventHandler<BridgeMessage>? MessageReceived;
    public event EventHandler<SourceStatus>? StatusChanged;

    public Guid SourceId => _source.Id;
    public Source Source => _source;
    public WebView2 WebView => _webView;
    public bool IsReady => _initialized && _webView?.CoreWebView2 is not null;
    public bool IsAgentInjected => _agentInjected;

    public WebView2Host(Source source, ILogger log)
    {
        _source = source;
        _log = log;

        // Isolated UserDataFolder per source (cookie/session isolation)
        var baseFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ChartWatcher", "webview_data");
        _userDataFolder = Path.Combine(baseFolder,
            string.IsNullOrWhiteSpace(source.UserDataFolder)
                ? source.Id.ToString("N")
                : source.UserDataFolder);
        Directory.CreateDirectory(_userDataFolder);
    }

    /// <summary>
    /// Creates the WebView2 control and initializes the environment.
    /// Must be called on the UI thread.
    /// </summary>
    public async Task<WebView2> InitializeAsync()
    {
        _webView = new WebView2
        {
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch,
        };

        // Create environment with isolated user data
        var env = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: _userDataFolder,
            options: new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments = "--disable-gpu-vsync --disable-frame-rate-limit"
            });

        await _webView.EnsureCoreWebView2Async(env);

        // Configure settings for market dashboard use
        var settings = _webView.CoreWebView2.Settings;
        settings.IsStatusBarEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreDevToolsEnabled = true; // Keep for debugging
        settings.IsZoomControlEnabled = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsBuiltInErrorPageEnabled = false;

        // Wire events
        _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
        _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        _webView.CoreWebView2.ProcessFailed += OnProcessFailed;

        // Pre-load agent JS
        var agentPath = Path.Combine(AppContext.BaseDirectory, "Assets", "chartwatch-agent.js");
        if (File.Exists(agentPath))
            _cachedAgentJs = await File.ReadAllTextAsync(agentPath);
        else
            _log.Warning("Agent JS not found at {Path}", agentPath);

        _initialized = true;
        _log.Information("WebView2Host initialized for {Name} (data: {Folder})",
            _source.Name, Path.GetFileName(_userDataFolder));

        return _webView;
    }

    /// <summary>
    /// Navigate to the source URL.
    /// </summary>
    public void Navigate()
    {
        if (!IsReady) return;
        StatusChanged?.Invoke(this, SourceStatus.Loading);
        _agentInjected = false;
        _webView.CoreWebView2.Navigate(_source.EntryUrl);
        _log.Information("Navigating {Name} → {Url}", _source.Name, _source.EntryUrl);
    }

    /// <summary>
    /// Navigate to a custom URL (for WholeSite card mode).
    /// </summary>
    public void NavigateTo(string url)
    {
        if (!IsReady) return;
        StatusChanged?.Invoke(this, SourceStatus.Loading);
        _agentInjected = false;
        _webView.CoreWebView2.Navigate(url);
    }

    /// <summary>
    /// Reload the current page.
    /// </summary>
    public void Reload()
    {
        if (!IsReady) return;
        StatusChanged?.Invoke(this, SourceStatus.Loading);
        _agentInjected = false;
        _webView.CoreWebView2.Reload();
    }

    /// <summary>
    /// Inject the JS agent into the current page.
    /// </summary>
    public async Task InjectAgentAsync()
    {
        if (!IsReady || _agentInjected || _cachedAgentJs is null) return;

        try
        {
            await _webView.CoreWebView2.ExecuteScriptAsync(_cachedAgentJs);
            _agentInjected = true;
            _log.Information("Agent injected into {Name}", _source.Name);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to inject agent into {Name}", _source.Name);
        }
    }

    /// <summary>
    /// Send a command to the JS agent via the bridge protocol.
    /// </summary>
    public void SendCommand(BridgeCommand command)
    {
        if (!IsReady || !_agentInjected) return;

        try
        {
            var json = JsonSerializer.Serialize(command, _jsonOpts);
            _webView.CoreWebView2.PostWebMessageAsJson(json);
            _log.Debug("→ {Name}: {Cmd}", _source.Name, command.Cmd);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to send command to {Name}", _source.Name);
        }
    }

    /// <summary>
    /// Execute arbitrary JS (for crop CSS injection, etc.)
    /// </summary>
    public async Task<string?> ExecuteScriptAsync(string script)
    {
        if (!IsReady) return null;
        try
        {
            return await _webView.CoreWebView2.ExecuteScriptAsync(script);
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Script execution failed on {Name}", _source.Name);
            return null;
        }
    }

    /// <summary>
    /// Open DevTools for this source's webview.
    /// </summary>
    public void OpenDevTools()
    {
        if (IsReady)
            _webView.CoreWebView2.OpenDevToolsWindow();
    }

    // ── Event handlers ──

    private async void OnNavigationCompleted(CoreWebView2 sender,
        CoreWebView2NavigationCompletedEventArgs args)
    {
        if (args.IsSuccess)
        {
            StatusChanged?.Invoke(this, SourceStatus.Ready);
            _log.Information("✓ {Name} loaded ({Url})", _source.Name, sender.Source);

            // Auto-inject agent after successful navigation
            await InjectAgentAsync();
        }
        else
        {
            StatusChanged?.Invoke(this, SourceStatus.Error);
            _log.Error("✗ {Name} failed: {Status}", _source.Name, args.WebErrorStatus);
        }
    }

    private void OnWebMessageReceived(CoreWebView2 sender,
        CoreWebView2WebMessageReceivedEventArgs args)
    {
        try
        {
            var json = args.WebMessageAsJson;
            var msg = JsonSerializer.Deserialize<BridgeMessage>(json, _jsonOpts);

            if (msg is not null)
            {
                msg.SourceId = _source.Id;
                MessageReceived?.Invoke(this, msg);
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to parse web message from {Name}", _source.Name);
        }
    }

    private void OnProcessFailed(CoreWebView2 sender,
        CoreWebView2ProcessFailedEventArgs args)
    {
        StatusChanged?.Invoke(this, SourceStatus.Error);
        _log.Error("WebView2 process failed for {Name}: {Kind}",
            _source.Name, args.ProcessFailedKind);
    }

    public async ValueTask DisposeAsync()
    {
        if (_webView?.CoreWebView2 is not null)
        {
            _webView.CoreWebView2.NavigationCompleted -= OnNavigationCompleted;
            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
            _webView.CoreWebView2.ProcessFailed -= OnProcessFailed;
        }
        _webView?.Close();
        _log.Information("WebView2Host disposed: {Name}", _source.Name);
    }
}
