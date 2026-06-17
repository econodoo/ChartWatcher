using CommunityToolkit.Mvvm.ComponentModel;
using ChartWatcher.Core.Components;
using ChartWatcher.Core.Workspaces;

namespace ChartWatcher.Application.ViewModels;

public partial class ComponentViewModel : ObservableObject
{
    public ComponentViewModel(Component component, Placement placement)
    {
        Component = component;
        Placement = placement;
    }

    public Component Component { get; }
    public Placement Placement { get; }

    public Guid Id => Component.Id;
    public string Title
    {
        get => Component.Title;
        set { Component.Title = value; OnPropertyChanged(); }
    }

    public ComponentType Type => Component.Type;
    public RenderMode RenderMode => Component.RenderMode;
    public Guid SourceId => Component.SourceId;

    // Grid position (bound by the dashboard grid)
    public int Row
    {
        get => Placement.Row;
        set { Placement.Row = value; OnPropertyChanged(); }
    }
    public int Col
    {
        get => Placement.Col;
        set { Placement.Col = value; OnPropertyChanged(); }
    }
    public int RowSpan
    {
        get => Placement.RowSpan;
        set { Placement.RowSpan = value; OnPropertyChanged(); }
    }
    public int ColSpan
    {
        get => Placement.ColSpan;
        set { Placement.ColSpan = value; OnPropertyChanged(); }
    }

    // Live content (HTML from mutation watcher)
    [ObservableProperty] private string _liveHtml = string.Empty;
    [ObservableProperty] private DateTime _lastUpdated;
    [ObservableProperty] private bool _isStale;
    [ObservableProperty] private string _statusIndicator = "●";
}
