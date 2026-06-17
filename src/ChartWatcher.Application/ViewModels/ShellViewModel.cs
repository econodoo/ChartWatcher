using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ChartWatcher.Core.Sources;
using ChartWatcher.Core.Workspaces;
using ChartWatcher.Core.Components;
using ChartWatcher.Application.Services;

namespace ChartWatcher.Application.ViewModels;

public partial class ShellViewModel : ObservableObject
{
    private readonly ISourceRepository _sourceRepo;
    private readonly IWorkspaceRepository _workspaceRepo;
    private readonly IComponentRepository _componentRepo;
    private readonly ThemeService _themeService;
    private readonly ISourceHub _sourceHub;

    public ShellViewModel(
        ISourceRepository sourceRepo,
        IWorkspaceRepository workspaceRepo,
        IComponentRepository componentRepo,
        ThemeService themeService,
        ISourceHub sourceHub)
    {
        _sourceRepo = sourceRepo;
        _workspaceRepo = workspaceRepo;
        _componentRepo = componentRepo;
        _themeService = themeService;
        _sourceHub = sourceHub;

        _themeService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ThemeService.CurrentTheme))
                OnPropertyChanged(nameof(CurrentTheme));
        };

        _sourceHub.StatusChanged += OnSourceStatusChanged;
    }

    // --- Observable state ---

    [ObservableProperty] private Workspace? _workspace;
    [ObservableProperty] private TabViewModel? _activeTab;
    [ObservableProperty] private bool _leftPanelOpen = true;
    [ObservableProperty] private bool _rightPanelOpen = true;
    [ObservableProperty] private string _statusText = "Ready";
    [ObservableProperty] private bool _isDesignMode = true;

    public ObservableCollection<TabViewModel> Tabs { get; } = [];
    public ObservableCollection<Source> Sources { get; } = [];
    public ObservableCollection<Component> LibraryComponents { get; } = [];
    public ObservableCollection<SourceStatusItem> SourceStatuses { get; } = [];

    public string CurrentTheme => _themeService.CurrentTheme;

    // --- Initialization ---

    public async Task InitializeAsync()
    {
        var workspace = await _workspaceRepo.GetActiveAsync();
        if (workspace is null)
        {
            workspace = new Workspace { Name = "Default" };
            workspace.Tabs.Add(new Tab
            {
                WorkspaceId = workspace.Id,
                Title = "Morning",
                OrderIndex = 0
            });
            workspace.ActiveTabId = workspace.Tabs[0].Id;
            await _workspaceRepo.SaveAsync(workspace);
        }
        Workspace = workspace;

        // Load sources
        var sources = await _sourceRepo.GetAllAsync();
        Sources.Clear();
        foreach (var s in sources) Sources.Add(s);

        // Seed SSI if no sources
        if (Sources.Count == 0)
        {
            var ssi = new Source
            {
                Name = "SSI iBoard",
                EntryUrl = "https://iboard.ssi.com.vn",
                ProviderId = "ssi",
                LoginCapability = LoginCapability.Anonymous,
                UserDataFolder = "ssi_data"
            };
            await _sourceRepo.SaveAsync(ssi);
            Sources.Add(ssi);
        }

        // Load library components
        var libItems = await _componentRepo.GetLibraryItemsAsync();
        LibraryComponents.Clear();
        foreach (var c in libItems) LibraryComponents.Add(c);

        // Build tabs
        Tabs.Clear();
        foreach (var tab in workspace.Tabs.OrderBy(t => t.OrderIndex))
        {
            var vm = new TabViewModel(tab, _componentRepo, _workspaceRepo);
            await vm.LoadPlacementsAsync();
            Tabs.Add(vm);
        }

        ActiveTab = Tabs.FirstOrDefault(t => t.Tab.Id == workspace.ActiveTabId) ?? Tabs.FirstOrDefault();
    }

    // --- Commands ---

    [RelayCommand]
    private async Task AddTabAsync()
    {
        if (Workspace is null) return;
        var tab = new Tab
        {
            WorkspaceId = Workspace.Id,
            Title = $"Tab {Tabs.Count + 1}",
            OrderIndex = Tabs.Count
        };
        Workspace.Tabs.Add(tab);
        await _workspaceRepo.SaveTabAsync(tab);

        var vm = new TabViewModel(tab, _componentRepo, _workspaceRepo);
        Tabs.Add(vm);
        ActiveTab = vm;
    }

    [RelayCommand]
    private async Task CloseTabAsync(TabViewModel? tabVm)
    {
        if (tabVm is null || Tabs.Count <= 1) return;
        Tabs.Remove(tabVm);
        if (Workspace is not null)
        {
            Workspace.Tabs.RemoveAll(t => t.Id == tabVm.Tab.Id);
            await _workspaceRepo.DeleteTabAsync(tabVm.Tab.Id);
        }
        if (ActiveTab == tabVm)
            ActiveTab = Tabs.FirstOrDefault();
    }

    [RelayCommand]
    private void CycleTheme() => _themeService.CycleTheme();

    [RelayCommand]
    private void ToggleLeftPanel() => LeftPanelOpen = !LeftPanelOpen;

    [RelayCommand]
    private void ToggleRightPanel() => RightPanelOpen = !RightPanelOpen;

    [RelayCommand]
    private void ToggleDesignMode()
    {
        IsDesignMode = !IsDesignMode;
        if (ActiveTab is not null)
            ActiveTab.IsDesignMode = IsDesignMode;
    }

    [RelayCommand]
    private async Task AddSourceAsync(string url)
    {
        var source = new Source
        {
            Name = new Uri(url).Host,
            EntryUrl = url,
            ProviderId = "custom"
        };
        await _sourceRepo.SaveAsync(source);
        Sources.Add(source);
    }

    [RelayCommand]
    private async Task StartSourceAsync(Source source)
    {
        await _sourceHub.StartSourceAsync(source);
    }

    // --- Source status tracking ---

    private void OnSourceStatusChanged(object? sender, SourceStatusChangedArgs e)
    {
        var existing = SourceStatuses.FirstOrDefault(s => s.SourceId == e.SourceId);
        if (existing is not null)
        {
            existing.Status = e.Status;
            existing.Message = e.Message;
        }
        else
        {
            SourceStatuses.Add(new SourceStatusItem
            {
                SourceId = e.SourceId,
                SourceName = Sources.FirstOrDefault(s => s.Id == e.SourceId)?.Name ?? "Unknown",
                Status = e.Status,
                Message = e.Message
            });
        }
        StatusText = $"Sources: {SourceStatuses.Count(s => s.Status == SourceStatus.Ready)} ready";
    }
}

public partial class SourceStatusItem : ObservableObject
{
    public Guid SourceId { get; init; }
    public string SourceName { get; init; } = string.Empty;
    [ObservableProperty] private SourceStatus _status;
    [ObservableProperty] private string? _message;
}
