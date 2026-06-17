using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using ChartWatcher.Application.Services;
using ChartWatcher.Core.Sources;
using ChartWatcher.Core.Stickers;
using ChartWatcher.UI.WebView;
using Serilog;
using Windows.Graphics;

namespace ChartWatcher.UI.Windows;

public sealed partial class PickerWindow : Window
{
    private WebView2Host? _host;
    private readonly List<PickResult> _picks = [];
    private readonly TaskCompletionSource<List<PickResult>?> _completion = new();

    public Task<List<PickResult>?> PickTask => _completion.Task;

    public PickerWindow()
    {
        this.InitializeComponent();

        var appWindow = GetAppWindow();
        if (appWindow is not null)
        {
            appWindow.Resize(new SizeInt32(1400, 900));
            appWindow.Title = "Chart Watcher — Pick Element";
        }
    }

    /// <summary>
    /// Start picking from a source. Creates a dedicated WebView2Host for the picker.
    /// </summary>
    public async Task StartPickingAsync(Source source)
    {
        TxtSourceName.Text = source.Name;

        _host = new WebView2Host(source, Log.Logger);
        var webView = await _host.InitializeAsync();

        // Add WebView2 to the container
        WebViewContainer.Children.Add(webView);

        // Listen for bridge messages
        _host.MessageReceived += OnBridgeMessage;
        _host.StatusChanged += OnStatusChanged;

        // Navigate to source URL
        _host.Navigate();
    }

    private void OnStatusChanged(object? sender, SourceStatus status)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (status)
            {
                case SourceStatus.Loading:
                    StatusDot.Fill = GetBrush("StatusDotLoading");
                    TxtPickStatus.Text = "Loading source page...";
                    break;
                case SourceStatus.Ready:
                    StatusDot.Fill = GetBrush("StatusDotReady");
                    TxtPickStatus.Text = "Page loaded. Entering picker mode...";
                    // Enter pick mode after agent is injected
                    EnterPickMode();
                    break;
                case SourceStatus.Error:
                    StatusDot.Fill = GetBrush("StatusDotError");
                    TxtPickStatus.Text = "Failed to load source page.";
                    break;
            }
        });
    }

    private async void EnterPickMode()
    {
        if (_host is null || !_host.IsAgentInjected) return;

        // Wait a moment for the page to stabilize
        await Task.Delay(500);

        _host.SendCommand(new BridgeCommand { Cmd = "enterPick" });
        TxtPickStatus.Text = "Hover elements to highlight. Click to select.";
    }

    private void OnBridgeMessage(object? sender, BridgeMessage msg)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            switch (msg.Evt)
            {
                case "picked":
                    HandlePicked(msg);
                    break;
                case "ready":
                    TxtPickStatus.Text = "Agent ready. Entering picker mode...";
                    EnterPickMode();
                    break;
            }
        });
    }

    private void HandlePicked(BridgeMessage msg)
    {
        // Build SelectorCascade from the picked element
        var cascade = new SelectorCascade();
        if (msg.Cascade.HasValue)
        {
            try
            {
                var cascadeArray = msg.Cascade.Value.EnumerateArray();
                foreach (var item in cascadeArray)
                {
                    var strategy = item.GetProperty("strategy").GetString() switch
                    {
                        "id" => SelectorStrategy.ElementId,
                        "data" => SelectorStrategy.DataAttribute,
                        "css" => SelectorStrategy.CssPath,
                        "xpath" => SelectorStrategy.XPath,
                        "text" => SelectorStrategy.TextMatch,
                        _ => SelectorStrategy.CssPath
                    };
                    cascade.Selectors.Add(new Selector
                    {
                        Strategy = strategy,
                        Expression = item.GetProperty("expression").GetString() ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to parse cascade from picker");
            }
        }

        var result = new PickResult
        {
            Cascade = cascade,
            InnerText = msg.InnerText ?? "",
            Attributes = msg.Attrs?.ToString() ?? "{}",
            BoundingRect = msg.Rect?.ToString() ?? "{}"
        };

        _picks.Add(result);
        TxtPickedCount.Text = $"{_picks.Count} picked";
        BtnDonePick.IsEnabled = true;

        TxtPickStatus.Text = $"Picked: \"{Truncate(result.InnerText, 60)}\" — Click more or press Done.";
        Log.Information("Element picked: {Text} ({Selectors} selectors)",
            Truncate(result.InnerText, 40), cascade.Selectors.Count);
    }

    private void BtnDone_Click(object sender, RoutedEventArgs e)
    {
        ExitPickMode();
        _completion.TrySetResult(_picks);
        this.Close();
    }

    private void BtnCancel_Click(object sender, RoutedEventArgs e)
    {
        ExitPickMode();
        _completion.TrySetResult(null);
        this.Close();
    }

    private void ExitPickMode()
    {
        _host?.SendCommand(new BridgeCommand { Cmd = "exitPick" });
    }

    private static string Truncate(string text, int maxLen) =>
        text.Length <= maxLen ? text : text[..(maxLen - 1)] + "…";

    private static SolidColorBrush GetBrush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var val)
            && val is SolidColorBrush brush)
            return brush;
        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    private AppWindow? GetAppWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var wndId = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(wndId);
    }
}

/// <summary>
/// Result of one element pick.
/// </summary>
public sealed class PickResult
{
    public SelectorCascade Cascade { get; set; } = new();
    public string InnerText { get; set; } = string.Empty;
    public string Attributes { get; set; } = "{}";
    public string BoundingRect { get; set; } = "{}";
}
