using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChartWatcher.Core.Components;
using ChartWatcher.Core.Workspaces;

namespace ChartWatcher.Application.ViewModels;

public partial class TabViewModel : ObservableObject
{
    private readonly IComponentRepository _componentRepo;
    private readonly IWorkspaceRepository _workspaceRepo;

    public TabViewModel(Tab tab, IComponentRepository componentRepo, IWorkspaceRepository workspaceRepo)
    {
        Tab = tab;
        _componentRepo = componentRepo;
        _workspaceRepo = workspaceRepo;
    }

    public Tab Tab { get; }
    public Guid Id => Tab.Id;
    public string Title
    {
        get => Tab.Title;
        set { Tab.Title = value; OnPropertyChanged(); }
    }

    public int GridCols => Tab.GridSpec.Cols;
    public int GridRows => Tab.GridSpec.Rows;
    public int GutterPx => Tab.GridSpec.GutterPx;

    [ObservableProperty] private bool _isDesignMode = true;
    [ObservableProperty] private ComponentViewModel? _selectedComponent;

    public ObservableCollection<ComponentViewModel> Components { get; } = [];

    public async Task LoadPlacementsAsync()
    {
        Components.Clear();
        foreach (var placement in Tab.Placements)
        {
            var comp = await _componentRepo.GetByIdAsync(placement.ComponentId);
            if (comp is not null)
            {
                Components.Add(new ComponentViewModel(comp, placement));
            }
        }
    }

    [RelayCommand]
    private async Task AddComponentAsync(Component component)
    {
        var placement = new Placement
        {
            TabId = Tab.Id,
            ComponentId = component.Id,
            Row = FindFirstEmptyRow(),
            Col = 0,
            RowSpan = 2,
            ColSpan = 4
        };
        Tab.Placements.Add(placement);
        await _workspaceRepo.SavePlacementAsync(placement);
        Components.Add(new ComponentViewModel(component, placement));
    }

    [RelayCommand]
    private async Task RemoveComponentAsync(ComponentViewModel? vm)
    {
        if (vm is null) return;
        Components.Remove(vm);
        Tab.Placements.RemoveAll(p => p.Id == vm.Placement.Id);
        await _workspaceRepo.DeletePlacementAsync(vm.Placement.Id);
        if (SelectedComponent == vm) SelectedComponent = null;
    }

    private int FindFirstEmptyRow()
    {
        if (Components.Count == 0) return 0;
        return Components.Max(c => c.Row + c.RowSpan);
    }
}
