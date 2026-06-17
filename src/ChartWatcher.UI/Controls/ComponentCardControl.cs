using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using ChartWatcher.Application.ViewModels;
using ChartWatcher.Core.Components;

namespace ChartWatcher.UI.Controls;

/// <summary>
/// A themable card that renders one Component on the dashboard grid.
/// Supports WholeSite (WebView2), Crop, and Clone render modes.
/// In design mode: shows drag handle, title bar, resize grips.
/// In view mode: content only.
/// </summary>
public sealed class ComponentCardControl : Grid
{
    private readonly ComponentViewModel _vm;
    private bool _isDesignMode = true;
    private readonly Grid _titleBar;
    private readonly TextBlock _titleText;
    private readonly Border _contentBorder;
    private readonly TextBlock _contentPlaceholder;
    private readonly Grid _designOverlay;
    private readonly Grid _resizeGrip;
    private readonly Button _deleteBtn;
    private readonly Button _configBtn;

    public event EventHandler<ComponentViewModel>? Selected;
    public event EventHandler<ComponentViewModel>? DeleteRequested;

    public ComponentViewModel ViewModel => _vm;

    public ComponentCardControl(ComponentViewModel vm)
    {
        _vm = vm;

        // Card root style
        CornerRadius = GetCornerRadius("CardCornerRadius");
        Background = GetBrush("BackgroundCard");
        BorderBrush = GetBrush("BorderBrush");
        BorderThickness = new Thickness(1);
        Padding = new Thickness(0);

        RowDefinitions.Add(new RowDefinition { Height = new GridLength(32) }); // Title bar
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Content

        // ── Title bar ──
        _titleBar = new Grid
        {
            Background = GetBrush("BackgroundDim"),
            Padding = new Thickness(8, 4, 4, 4),
            ColumnSpacing = 4
        };
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Title text
        _titleText = new TextBlock
        {
            Text = vm.Title,
            FontSize = 11,
            Foreground = GetBrush("ForegroundSecondary"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Grid.SetColumn(_titleText, 0);
        _titleBar.Children.Add(_titleText);

        // Render mode badge
        var modeBadge = new Border
        {
            Background = GetBrush("AccentBrush"),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 1, 4, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.7
        };
        var modeText = new TextBlock
        {
            Text = vm.Type switch
            {
                ComponentType.WholeSite => "SITE",
                ComponentType.Crop => "CROP",
                ComponentType.Clone => "CLONE",
                _ => "?"
            },
            FontSize = 9,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = GetBrush("ForegroundPrimary")
        };
        modeBadge.Child = modeText;
        Grid.SetColumn(modeBadge, 1);
        _titleBar.Children.Add(modeBadge);

        // Config button
        _configBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE713", FontSize = 11 },
            Padding = new Thickness(4, 2, 4, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTipService.SetToolTip(_configBtn, "Configure");
        Grid.SetColumn(_configBtn, 2);
        _titleBar.Children.Add(_configBtn);

        // Delete button
        _deleteBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE74D", FontSize = 11 },
            Padding = new Thickness(4, 2, 4, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        _deleteBtn.Click += (_, _) => DeleteRequested?.Invoke(this, _vm);
        ToolTipService.SetToolTip(_deleteBtn, "Remove");
        Grid.SetColumn(_deleteBtn, 3);
        _titleBar.Children.Add(_deleteBtn);

        Grid.SetRow(_titleBar, 0);
        Children.Add(_titleBar);

        // ── Content area ──
        _contentBorder = new Border
        {
            Background = GetBrush("BackgroundCard"),
            Padding = new Thickness(8)
        };

        _contentPlaceholder = new TextBlock
        {
            Text = GetContentMessage(vm),
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 12,
            Foreground = GetBrush("ForegroundDim"),
            TextAlignment = TextAlignment.Center
        };
        _contentBorder.Child = _contentPlaceholder;

        Grid.SetRow(_contentBorder, 1);
        Children.Add(_contentBorder);

        // ── Design mode overlay (resize grip) ──
        _designOverlay = new Grid
        {
            IsHitTestVisible = false,
            Opacity = 0.3
        };

        _resizeGrip = new Grid
        {
            Width = 16, Height = 16,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 4, 4)
        };
        _resizeGrip.Children.Add(new FontIcon
        {
            Glyph = "\uE740",
            FontSize = 10,
            Foreground = GetBrush("ForegroundDim")
        });
        _designOverlay.Children.Add(_resizeGrip);
        Grid.SetRow(_designOverlay, 1);
        Children.Add(_designOverlay);

        // ── Interaction ──
        PointerPressed += OnCardPointerPressed;
        PointerEntered += OnCardPointerEntered;
        PointerExited += OnCardPointerExited;
    }

    private static string GetContentMessage(ComponentViewModel vm) => vm.Type switch
    {
        ComponentType.WholeSite =>
            $"WebView2 will load:\n{(vm.Component.SourceId != Guid.Empty ? "Source connected" : "No source")}",
        ComponentType.Crop =>
            "Crop mode — pick elements\nto isolate from source page",
        ComponentType.Clone =>
            "Clone mode — pick elements\nto mirror with custom CSS",
        _ => "Unknown component type"
    };

    public void SetDesignMode(bool design)
    {
        _isDesignMode = design;
        _titleBar.Visibility = design ? Visibility.Visible : Visibility.Collapsed;
        _designOverlay.Visibility = design ? Visibility.Visible : Visibility.Collapsed;
        BorderThickness = design ? new Thickness(1) : new Thickness(0);
    }

    public void RefreshTheme()
    {
        Background = GetBrush("BackgroundCard");
        BorderBrush = GetBrush("BorderBrush");
        _titleBar.Background = GetBrush("BackgroundDim");
        _titleText.Foreground = GetBrush("ForegroundSecondary");
        _contentPlaceholder.Foreground = GetBrush("ForegroundDim");
        _contentBorder.Background = GetBrush("BackgroundCard");
    }

    /// <summary>
    /// Update the card with live HTML content from a mutation event.
    /// </summary>
    public void UpdateLiveContent(string html, DateTime timestamp)
    {
        _vm.LiveHtml = html;
        _vm.LastUpdated = timestamp;
        _vm.IsStale = false;

        // For Clone mode: show the HTML text content (simplified)
        if (_vm.Type == ComponentType.Clone && !string.IsNullOrWhiteSpace(html))
        {
            _contentPlaceholder.Text = StripHtml(html);
            _contentPlaceholder.Foreground = GetBrush("ForegroundPrimary");
        }

        // Update stale indicator
        _vm.StatusIndicator = "●";
    }

    /// <summary>
    /// Mark this card as stale (no recent updates).
    /// </summary>
    public void MarkStale()
    {
        _vm.IsStale = true;
        _vm.StatusIndicator = "◌";
        _contentPlaceholder.Foreground = GetBrush("ForegroundDim");
    }

    /// <summary>
    /// Set a WebView2 control as the card content (for WholeSite/Crop modes).
    /// </summary>
    public void SetWebViewContent(Microsoft.UI.Xaml.Controls.WebView2 webView)
    {
        _contentBorder.Child = webView;
    }

    private static string StripHtml(string html)
    {
        // Simple HTML tag removal for text preview
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length > 500 ? text[..497] + "..." : text;
    }

    private void OnCardPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        Selected?.Invoke(this, _vm);

        // Highlight selection
        BorderBrush = GetBrush("BorderActiveBrush");
        BorderThickness = new Thickness(2);

        // Deselect siblings
        if (Parent is Canvas canvas)
        {
            foreach (var child in canvas.Children)
            {
                if (child is ComponentCardControl other && other != this)
                {
                    other.BorderBrush = GetBrush("BorderBrush");
                    other.BorderThickness = new Thickness(1);
                }
            }
        }
    }

    private void OnCardPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (_isDesignMode)
            Background = GetBrush("BackgroundCardHover");
    }

    private void OnCardPointerExited(object sender, PointerRoutedEventArgs e)
    {
        Background = GetBrush("BackgroundCard");
    }

    // ── Theme resource helpers ──

    private static SolidColorBrush GetBrush(string key)
    {
        if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out var val)
            && val is SolidColorBrush brush)
            return brush;
        return new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }

    private static CornerRadius GetCornerRadius(string key)
    {
        if (Microsoft.UI.Xaml.Application.Current.Resources.TryGetValue(key, out var val)
            && val is CornerRadius cr)
            return cr;
        return new CornerRadius(8);
    }
}
