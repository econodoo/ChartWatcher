using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using ChartWatcher.Application.Services;
using ChartWatcher.Application.ViewModels;
using ChartWatcher.Core.Components;
using ChartWatcher.Core.Sources;
using ChartWatcher.UI.Controls;
using ChartWatcher.UI.WebView;
using Serilog;
using Windows.Graphics;

namespace ChartWatcher.UI.Windows;

public sealed partial class ShellWindow : Window
{
    private ShellViewModel _vm = null!;
    private ThemeService _themeService = null!;
    private ISourceHub _sourceHub = null!;
    private bool _leftDragging, _rightDragging;
    private double _dragStartX;
    private readonly DispatcherTimer _memoryTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly List<ComponentCardControl> _cardControls = [];
    private readonly Dictionary<Guid, WebView2Host> _sourceHosts = [];
    private readonly Dictionary<Guid, Ellipse> _statusDotMap = [];

    public ShellWindow()
    {
        this.InitializeComponent();

        // Window sizing
        var appWindow = GetAppWindow();
        if (appWindow is not null)
        {
            appWindow.Resize(new SizeInt32(1600, 950));
            appWindow.Title = "Chart Watcher";
        }

        // Memory usage timer
        _memoryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _memoryTimer.Tick += (_, _) => UpdateMemoryDisplay();
        _memoryTimer.Start();

        // Clock timer for status bar
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();

        // Initialize after load
        this.Content.Loaded += OnContentLoaded;

        // Reposition cards when canvas resizes
        DashboardCanvas.SizeChanged += (_, _) =>
        {
            foreach (var card in _cardControls)
                PositionCard(card, card.ViewModel);
            RedrawGridLines();
        };
    }

    private async void OnContentLoaded(object sender, RoutedEventArgs e)
    {
        _vm = App.Services.GetRequiredService<ShellViewModel>();
        _themeService = App.Services.GetRequiredService<ThemeService>();
        _sourceHub = App.Services.GetRequiredService<ISourceHub>();

        _themeService.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ThemeService.CurrentTheme))
                ApplyTheme(_themeService.CurrentTheme);
        };

        // Wire mutation routing from SourceHub → component cards
        _sourceHub.MutationReceived += OnMutationReceived;
        _sourceHub.StatusChanged += OnSourceStatusChanged;

        await _vm.InitializeAsync();
        PopulateUI();

        // Auto-launch all sources with WebView2
        foreach (var source in _vm.Sources)
            await LaunchSourceAsync(source);

        Log.Information("Shell initialized with {TabCount} tabs, {SourceCount} sources",
            _vm.Tabs.Count, _vm.Sources.Count);
    }

    // ───────── THEME SWITCHING ─────────

    private void ApplyTheme(string themeName)
    {
        var dict = new ResourceDictionary
        {
            Source = new Uri($"ms-appx:///Themes/{themeName}.xaml")
        };

        var mergedDicts = Microsoft.UI.Xaml.Application.Current.Resources.MergedDictionaries;
        // Remove old theme (keep XamlControlsResources at index 0)
        if (mergedDicts.Count > 1)
            mergedDicts.RemoveAt(mergedDicts.Count - 1);
        mergedDicts.Add(dict);

        TxtCurrentTheme.Text = themeName;
        Log.Information("Theme switched to {Theme}", themeName);

        // Force refresh on all card controls
        foreach (var card in _cardControls)
            card.RefreshTheme();

        RedrawGridLines();
    }

    private void ThemeColorful_Click(object s, RoutedEventArgs e) => _themeService.SetTheme("Colorful");
    private void ThemeStealth_Click(object s, RoutedEventArgs e) => _themeService.SetTheme("Stealth");
    private void ThemeColorBlind_Click(object s, RoutedEventArgs e) => _themeService.SetTheme("ColorBlind");

    // ───────── PANEL TOGGLING ─────────

    private void BtnToggleLeft_Click(object s, RoutedEventArgs e)
    {
        if (ColLeft.Width.Value > 0)
        {
            ColLeft.Width = new GridLength(0);
            PanelLeft.Visibility = Visibility.Collapsed;
        }
        else
        {
            ColLeft.Width = new GridLength(240);
            PanelLeft.Visibility = Visibility.Visible;
        }
    }

    private void BtnToggleRight_Click(object s, RoutedEventArgs e)
    {
        if (ColRight.Width.Value > 0)
        {
            ColRight.Width = new GridLength(0);
            PanelRight.Visibility = Visibility.Collapsed;
        }
        else
        {
            ColRight.Width = new GridLength(280);
            PanelRight.Visibility = Visibility.Visible;
        }
    }

    // ───────── SPLITTER DRAG ─────────

    private void LeftSplitter_PointerPressed(object s, PointerRoutedEventArgs e)
    {
        _leftDragging = true;
        _dragStartX = e.GetCurrentPoint((UIElement)s).Position.X;
        ((UIElement)s).CapturePointer(e.Pointer);
    }

    private void LeftSplitter_PointerMoved(object s, PointerRoutedEventArgs e)
    {
        if (!_leftDragging) return;
        var pos = e.GetCurrentPoint((UIElement)s).Position.X;
        var delta = pos - _dragStartX;
        var newWidth = Math.Max(160, Math.Min(400, ColLeft.Width.Value + delta));
        ColLeft.Width = new GridLength(newWidth);
    }

    private void RightSplitter_PointerPressed(object s, PointerRoutedEventArgs e)
    {
        _rightDragging = true;
        _dragStartX = e.GetCurrentPoint((UIElement)s).Position.X;
        ((UIElement)s).CapturePointer(e.Pointer);
    }

    private void RightSplitter_PointerMoved(object s, PointerRoutedEventArgs e)
    {
        if (!_rightDragging) return;
        var pos = e.GetCurrentPoint((UIElement)s).Position.X;
        var delta = _dragStartX - pos;
        var newWidth = Math.Max(200, Math.Min(450, ColRight.Width.Value + delta));
        ColRight.Width = new GridLength(newWidth);
    }

    private void Splitter_PointerReleased(object s, PointerRoutedEventArgs e)
    {
        _leftDragging = false;
        _rightDragging = false;
        ((UIElement)s).ReleasePointerCapture(e.Pointer);
    }

    private void Splitter_PointerEntered(object s, PointerRoutedEventArgs e)
    {
        // WinUI 3 cursor change requires ProtectedCursor on a derived control.
        // The splitter rectangle provides visual affordance instead.
    }

    // ───────── TAB MANAGEMENT ─────────

    private void DashboardTabs_AddTabClick(TabView sender, object args)
    {
        var tab = new TabViewItem
        {
            Header = $"Tab {DashboardTabs.TabItems.Count + 1}",
            IsClosable = true,
            IconSource = new SymbolIconSource { Symbol = Symbol.View }
        };
        DashboardTabs.TabItems.Add(tab);
        DashboardTabs.SelectedItem = tab;
        _vm?.AddTabCommand.Execute(null);
    }

    private void DashboardTabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (DashboardTabs.TabItems.Count <= 1) return;
        DashboardTabs.TabItems.Remove(args.Tab);
    }

    private void DashboardTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Rebuild grid for selected tab
        RedrawGridLines();
    }

    // ───────── DESIGN / VIEW MODE ─────────

    private void BtnDesignMode_Click(object sender, RoutedEventArgs e)
    {
        var isDesign = BtnDesignMode.IsChecked == true;
        TxtDesignMode.Text = isDesign ? "Design" : "View";
        TxtModeBadge.Text = isDesign ? "Design" : "View";
        GridLinesCanvas.Opacity = isDesign ? 0.15 : 0;

        foreach (var card in _cardControls)
            card.SetDesignMode(isDesign);
    }

    // ───────── ADD SOURCE DIALOG ─────────

    private async void BtnAddSource_Click(object sender, RoutedEventArgs e)
    {
        var urlBox = new TextBox
        {
            PlaceholderText = "https://iboard.ssi.com.vn",
            Header = "Source URL"
        };

        var nameBox = new TextBox
        {
            PlaceholderText = "SSI iBoard",
            Header = "Display Name",
            Margin = new Thickness(0, 12, 0, 0)
        };

        var panel = new StackPanel();
        panel.Children.Add(urlBox);
        panel.Children.Add(nameBox);

        var dialog = new ContentDialog
        {
            Title = "Add Source",
            Content = panel,
            PrimaryButtonText = "Add",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(urlBox.Text))
        {
            var source = new Source
            {
                Name = string.IsNullOrWhiteSpace(nameBox.Text)
                    ? new Uri(urlBox.Text).Host
                    : nameBox.Text,
                EntryUrl = urlBox.Text,
                ProviderId = "custom"
            };
            await App.Services.GetRequiredService<ISourceRepository>().SaveAsync(source);
            _vm.Sources.Add(source);

            // Start the source
            await _vm.StartSourceCommand.ExecuteAsync(source);

            AddSourceStatusDot(source);

            // Launch WebView2 for the source
            await LaunchSourceAsync(source);

            Log.Information("Source added: {Name} ({Url})", source.Name, source.EntryUrl);
        }
    }

    // ───────── ADD COMPONENT (WholeSite quick-add) ─────────

    private async void BtnAddComponent_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Sources.Count == 0)
        {
            await ShowMessage("No sources", "Add a source first before creating components.");
            return;
        }

        var sourceCombo = new ComboBox
        {
            Header = "Source",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        foreach (var s in _vm.Sources)
            sourceCombo.Items.Add(s.Name);
        if (sourceCombo.Items.Count > 0)
            sourceCombo.SelectedIndex = 0;

        var titleBox = new TextBox
        {
            Header = "Component Title",
            PlaceholderText = "e.g., SSI Full Board",
            Margin = new Thickness(0, 12, 0, 0)
        };

        var modeCombo = new ComboBox
        {
            Header = "Render Mode",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 12, 0, 0)
        };
        modeCombo.Items.Add("WholeSite");
        modeCombo.Items.Add("Crop (pick later)");
        modeCombo.Items.Add("Clone (pick later)");
        modeCombo.SelectedIndex = 0;

        var panel = new StackPanel();
        panel.Children.Add(sourceCombo);
        panel.Children.Add(titleBox);
        panel.Children.Add(modeCombo);

        var dialog = new ContentDialog
        {
            Title = "Add Component",
            Content = panel,
            PrimaryButtonText = "Create",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = this.Content.XamlRoot
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            var selectedSource = _vm.Sources[sourceCombo.SelectedIndex];
            var type = modeCombo.SelectedIndex switch
            {
                1 => ComponentType.Crop,
                2 => ComponentType.Clone,
                _ => ComponentType.WholeSite
            };

            var component = new Component
            {
                Title = string.IsNullOrWhiteSpace(titleBox.Text) ? selectedSource.Name : titleBox.Text,
                Type = type,
                RenderMode = (RenderMode)type,
                SourceId = selectedSource.Id,
                IsLibraryItem = true
            };

            // For Crop/Clone: launch picker to select elements
            if (type is ComponentType.Crop or ComponentType.Clone)
            {
                var picks = await LaunchPickerAsync(selectedSource);
                if (picks is null || picks.Count == 0)
                {
                    Log.Information("Picker cancelled — no component created");
                    return;
                }

                // Save picked selectors to the component
                component.Selectors = picks.Select(p => p.Cascade).ToList();
                if (string.IsNullOrWhiteSpace(component.Title) || component.Title == selectedSource.Name)
                {
                    var previewText = picks[0].InnerText;
                    component.Title = previewText.Length > 30 ? previewText[..27] + "…" : previewText;
                }
            }

            await App.Services.GetRequiredService<IComponentRepository>().SaveAsync(component);
            _vm.LibraryComponents.Add(component);

            // Auto-place on current tab
            if (_vm.ActiveTab is not null)
            {
                await _vm.ActiveTab.AddComponentCommand.ExecuteAsync(component);
                RebuildDashboardCards();

                // For Clone mode: send observe commands to the source agent
                if (type == ComponentType.Clone)
                    SendObserveCommands(selectedSource.Id, component);
            }

            Log.Information("Component created: {Title} ({Type}, {Selectors} selectors)",
                component.Title, type, component.Selectors.Count);
        }
        }
    }

    // ───────── INSPECTOR BINDING ─────────

    private void LayoutChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_vm?.ActiveTab?.SelectedComponent is null) return;
        var comp = _vm.ActiveTab.SelectedComponent;
        comp.Row = (int)NbRow.Value;
        comp.Col = (int)NbCol.Value;
        comp.RowSpan = (int)NbRowSpan.Value;
        comp.ColSpan = (int)NbColSpan.Value;
        RepositionCard(comp);
    }

    private void SelectComponent(ComponentViewModel vm)
    {
        if (_vm?.ActiveTab is null) return;
        _vm.ActiveTab.SelectedComponent = vm;

        InspectorEmpty.Visibility = Visibility.Collapsed;
        InspectorContent.Visibility = Visibility.Visible;

        TxtComponentTitle.Text = vm.Title;
        NbRow.Value = vm.Row;
        NbCol.Value = vm.Col;
        NbRowSpan.Value = vm.RowSpan;
        NbColSpan.Value = vm.ColSpan;

        var source = _vm.Sources.FirstOrDefault(s => s.Id == vm.SourceId);
        TxtSourceName.Text = source?.Name ?? "Unknown";
        TxtRenderMode.Text = vm.RenderMode.ToString();
        TxtLastUpdate.Text = $"Last update: {vm.LastUpdated:HH:mm:ss}";
    }

    // ───────── DASHBOARD RENDERING ─────────

    private void PopulateUI()
    {
        if (_vm is null) return;

        // Create tab items
        DashboardTabs.TabItems.Clear();
        foreach (var tabVm in _vm.Tabs)
        {
            var item = new TabViewItem
            {
                Header = tabVm.Title,
                IsClosable = _vm.Tabs.Count > 1,
                IconSource = new SymbolIconSource { Symbol = Symbol.View },
                Tag = tabVm
            };
            DashboardTabs.TabItems.Add(item);
        }
        if (DashboardTabs.TabItems.Count > 0)
            DashboardTabs.SelectedIndex = 0;

        // Add source status dots
        foreach (var source in _vm.Sources)
            AddSourceStatusDot(source);

        // Build cards
        RebuildDashboardCards();
        RedrawGridLines();
    }

    private void RebuildDashboardCards()
    {
        ComponentCanvas.Children.Clear();
        _cardControls.Clear();

        var tab = _vm?.ActiveTab;
        if (tab is null || tab.Components.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        EmptyState.Visibility = Visibility.Collapsed;

        foreach (var compVm in tab.Components)
        {
            var card = new ComponentCardControl(compVm);
            card.Selected += (_, vm) => SelectComponent(vm);
            card.DeleteRequested += async (_, vm) =>
            {
                await tab.RemoveComponentCommand.ExecuteAsync(vm);
                RebuildDashboardCards();
            };
            _cardControls.Add(card);
            ComponentCanvas.Children.Add(card);

            // Position based on grid
            PositionCard(card, compVm);

            // Mode-specific wiring
            switch (compVm.Type)
            {
                case ComponentType.WholeSite:
                    _ = EmbedWholeSiteWebViewAsync(card, compVm);
                    break;
                case ComponentType.Clone:
                    SendObserveCommands(compVm.SourceId, compVm.Component);
                    break;
                case ComponentType.Crop:
                    _ = EmbedCropWebViewAsync(card, compVm);
                    break;
            }
        }
    }

    /// <summary>
    /// Create a dedicated WebView2 inside a WholeSite card.
    /// </summary>
    private async Task EmbedWholeSiteWebViewAsync(ComponentCardControl card, ComponentViewModel vm)
    {
        try
        {
            var source = _vm.Sources.FirstOrDefault(s => s.Id == vm.SourceId);
            if (source is null) return;

            var host = new WebView2Host(source, Log.Logger);
            var webView = await host.InitializeAsync();
            card.SetWebViewContent(webView);
            host.Navigate();

            Log.Information("WholeSite WebView2 embedded for {Title}", vm.Title);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to embed WholeSite WebView2 for {Title}", vm.Title);
        }
    }

    /// <summary>
    /// Create a dedicated WebView2 for Crop mode with CSS surgery.
    /// </summary>
    private async Task EmbedCropWebViewAsync(ComponentCardControl card, ComponentViewModel vm)
    {
        try
        {
            var source = _vm.Sources.FirstOrDefault(s => s.Id == vm.SourceId);
            if (source is null) return;

            var host = new WebView2Host(source, Log.Logger);
            var webView = await host.InitializeAsync();
            card.SetWebViewContent(webView);

            // Navigate and inject crop CSS after load
            host.StatusChanged += async (_, status) =>
            {
                if (status == SourceStatus.Ready && vm.Component.Selectors.Count > 0)
                {
                    // Send crop command to agent
                    host.SendCommand(new BridgeCommand
                    {
                        Cmd = "applyCrop",
                        Cascades = vm.Component.Selectors.Select(s => s.Selectors.Select(sel => new
                        {
                            strategy = sel.Strategy.ToString().ToLower(),
                            expression = sel.Expression
                        }).ToList()).ToList()
                    });
                }
            };

            host.Navigate();
            Log.Information("Crop WebView2 embedded for {Title}", vm.Title);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to embed Crop WebView2 for {Title}", vm.Title);
        }
    }

    /// <summary>
    /// Send observe commands to the source's JS agent for Clone mode.
    /// </summary>
    private void SendObserveCommands(Guid sourceId, Component component)
    {
        if (!_sourceHosts.TryGetValue(sourceId, out var host)) return;
        if (!host.IsAgentInjected) return;

        for (int i = 0; i < component.Selectors.Count; i++)
        {
            var cascade = component.Selectors[i];
            var stickerId = $"{component.Id}:{i}";

            host.SendCommand(new BridgeCommand
            {
                Cmd = "observe",
                StickerId = stickerId,
                Cascade = cascade.Selectors.Select(s => new
                {
                    strategy = s.Strategy.ToString().ToLower(),
                    expression = s.Expression
                }).ToList()
            });
        }

        Log.Information("Sent {N} observe commands for {Title}",
            component.Selectors.Count, component.Title);
    }

    private void PositionCard(ComponentCardControl card, ComponentViewModel vm)
    {
        var canvasWidth = DashboardCanvas.ActualWidth - 16;
        var canvasHeight = DashboardCanvas.ActualHeight - 16;
        if (canvasWidth <= 0 || canvasHeight <= 0) return;

        var cellW = canvasWidth / 12.0;
        var cellH = canvasHeight / 8.0;

        Canvas.SetLeft(card, vm.Col * cellW + 4);
        Canvas.SetTop(card, vm.Row * cellH + 4);
        card.Width = vm.ColSpan * cellW - 8;
        card.Height = vm.RowSpan * cellH - 8;
    }

    private void RepositionCard(ComponentViewModel vm)
    {
        var card = _cardControls.FirstOrDefault(c => c.ViewModel.Id == vm.Id);
        if (card is not null)
            PositionCard(card, vm);
    }

    private void RedrawGridLines()
    {
        GridLinesCanvas.Children.Clear();
        var w = DashboardCanvas.ActualWidth - 16;
        var h = DashboardCanvas.ActualHeight - 16;
        if (w <= 0 || h <= 0) return;

        var borderBrush = TryFindBrush("BorderBrush")
            ?? new SolidColorBrush(Microsoft.UI.Colors.Gray);

        // Vertical lines (12 cols)
        for (int i = 0; i <= 12; i++)
        {
            var x = (w / 12.0) * i;
            GridLinesCanvas.Children.Add(new Line
            {
                X1 = x, Y1 = 0, X2 = x, Y2 = h,
                Stroke = borderBrush, StrokeThickness = 0.5,
                StrokeDashArray = [4, 4]
            });
        }
        // Horizontal lines (8 rows)
        for (int i = 0; i <= 8; i++)
        {
            var y = (h / 8.0) * i;
            GridLinesCanvas.Children.Add(new Line
            {
                X1 = 0, Y1 = y, X2 = w, Y2 = y,
                Stroke = borderBrush, StrokeThickness = 0.5,
                StrokeDashArray = [4, 4]
            });
        }
    }

    // ───────── STATUS BAR ─────────

    private void AddSourceStatusDot(Source source)
    {
        var dot = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        var ellipse = new Ellipse
        {
            Width = 8, Height = 8,
            Fill = TryFindBrush("StatusDotLoading") ?? new SolidColorBrush(Microsoft.UI.Colors.Yellow),
            VerticalAlignment = VerticalAlignment.Center
        };
        var name = new TextBlock
        {
            Text = source.Name,
            FontSize = 11,
            Foreground = TryFindBrush("ForegroundDim") ?? new SolidColorBrush(Microsoft.UI.Colors.Gray),
            VerticalAlignment = VerticalAlignment.Center
        };
        dot.Children.Add(ellipse);
        dot.Children.Add(name);
        StatusDots.Children.Add(dot);

        // Track for runtime status updates
        _statusDotMap[source.Id] = ellipse;
    }

    private void UpdateMemoryDisplay()
    {
        var mb = Process.GetCurrentProcess().WorkingSet64 / (1024.0 * 1024.0);
        TxtMemory.Text = $"{mb:F0} MB";
    }

    private void UpdateClock()
    {
        TxtLastMutation.Text = $"Updated {DateTime.Now:HH:mm:ss}";
    }

    // ───────── HELPERS ─────────

    private SolidColorBrush? TryFindBrush(string key)
    {
        if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out var val)
            && val is SolidColorBrush brush)
            return brush;
        return null;
    }

    private AppWindow? GetAppWindow()
    {
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
        var wndId = Win32Interop.GetWindowIdFromWindow(hwnd);
        return AppWindow.GetFromWindowId(wndId);
    }

    private async Task ShowMessage(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            XamlRoot = this.Content.XamlRoot
        };
        await dialog.ShowAsync();
    }

    // ───────── WEBVIEW2 SOURCE MANAGEMENT ─────────

    /// <summary>
    /// Create a hidden WebView2 for a source and navigate to its URL.
    /// </summary>
    private async Task LaunchSourceAsync(Source source)
    {
        if (_sourceHosts.ContainsKey(source.Id)) return;

        try
        {
            var host = new WebView2Host(source, Log.Logger);
            var webView = await host.InitializeAsync();

            // Add to hidden panel (WebView2 needs to be in visual tree to work)
            webView.Width = 1;
            webView.Height = 1;
            HiddenWebViewPanel.Children.Add(webView);
            _sourceHosts[source.Id] = host;

            // Wire status changes to UI
            host.StatusChanged += (_, status) =>
            {
                DispatcherQueue.TryEnqueue(() => UpdateSourceDot(source.Id, status));
            };

            // Wire mutations to component cards
            host.MessageReceived += (_, msg) =>
            {
                DispatcherQueue.TryEnqueue(() => RouteBridgeMessage(msg));
            };

            // Register with SourceHub
            await _sourceHub.StartSourceAsync(source);

            // Navigate
            host.Navigate();

            Log.Information("Source launched: {Name} → {Url}", source.Name, source.EntryUrl);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch source {Name}", source.Name);
            await ShowMessage("Source Error", $"Failed to launch {source.Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Update the status dot color for a source.
    /// </summary>
    private void UpdateSourceDot(Guid sourceId, SourceStatus status)
    {
        if (!_statusDotMap.TryGetValue(sourceId, out var dot)) return;

        var brushKey = status switch
        {
            SourceStatus.Ready => "StatusDotReady",
            SourceStatus.Loading => "StatusDotLoading",
            SourceStatus.Error => "StatusDotError",
            SourceStatus.Stale => "StatusDotLoading",
            _ => "StatusDotLoading"
        };
        dot.Fill = TryFindBrush(brushKey)
            ?? new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    /// <summary>
    /// Route bridge messages from sources to the appropriate component cards.
    /// </summary>
    private void RouteBridgeMessage(BridgeMessage msg)
    {
        switch (msg.Evt)
        {
            case "mutation":
                // Find all components bound to this source+sticker and update
                foreach (var card in _cardControls)
                {
                    if (card.ViewModel.SourceId == msg.SourceId)
                    {
                        card.UpdateLiveContent(msg.Html ?? "", DateTime.UtcNow);
                    }
                }
                TxtLastMutation.Text = $"Updated {DateTime.Now:HH:mm:ss}";
                break;

            case "ready":
                _sourceHub.ReportReady(msg.SourceId);
                break;

            case "picked":
                // Handled by PickerWindow
                break;
        }
    }

    /// <summary>
    /// Handle mutation events from SourceHub.
    /// </summary>
    private void OnMutationReceived(object? sender, MutationReceivedArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            foreach (var card in _cardControls)
            {
                if (card.ViewModel.SourceId == e.SourceId)
                {
                    card.UpdateLiveContent(e.Html, e.Timestamp);
                }
            }
        });
    }

    /// <summary>
    /// Handle source status changes from SourceHub.
    /// </summary>
    private void OnSourceStatusChanged(object? sender, SourceStatusChangedArgs e)
    {
        DispatcherQueue.TryEnqueue(() => UpdateSourceDot(e.SourceId, e.Status));
    }

    /// <summary>
    /// Launch the PickerWindow for a specific source.
    /// </summary>
    private async Task<List<PickResult>?> LaunchPickerAsync(Source source)
    {
        var picker = new PickerWindow();
        picker.Activate();
        await picker.StartPickingAsync(source);
        return await picker.PickTask;
    }
}
