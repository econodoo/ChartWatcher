namespace ChartWatcher.Core.Workspaces;

public sealed class Workspace
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Default";
    public List<Tab> Tabs { get; set; } = [];
    public Guid? ActiveTabId { get; set; }
    public bool LeftPanelCollapsed { get; set; }
    public bool RightPanelCollapsed { get; set; }
    public string CurrentTheme { get; set; } = "Colorful";
    public int ChattinessLevel { get; set; } = 2; // 0=Silent, 4=Live
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Tab
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public string Title { get; set; } = "New Tab";
    public int OrderIndex { get; set; }
    public GridSpec GridSpec { get; set; } = new();
    public List<Placement> Placements { get; set; } = [];
    public bool IsDesignMode { get; set; } = true;
}

public sealed class GridSpec
{
    public int Cols { get; set; } = 12;
    public int Rows { get; set; } = 8;
    public int GutterPx { get; set; } = 8;
}

public sealed class Placement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TabId { get; set; }
    public Guid ComponentId { get; set; }
    public int Row { get; set; }
    public int Col { get; set; }
    public int RowSpan { get; set; } = 2;
    public int ColSpan { get; set; } = 3;
}

public interface IWorkspaceRepository
{
    Task<Workspace?> GetActiveAsync();
    Task<Workspace?> GetByIdAsync(Guid id);
    Task SaveAsync(Workspace workspace);
    Task SaveTabAsync(Tab tab);
    Task SavePlacementAsync(Placement placement);
    Task DeletePlacementAsync(Guid id);
    Task DeleteTabAsync(Guid id);
}
