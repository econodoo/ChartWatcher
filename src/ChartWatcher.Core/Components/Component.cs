using ChartWatcher.Core.Stickers;
using ChartWatcher.Core.Thresholds;

namespace ChartWatcher.Core.Components;

public sealed class Component
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Untitled";
    public ComponentType Type { get; set; } = ComponentType.WholeSite;
    public Guid SourceId { get; set; }
    public RenderMode RenderMode { get; set; } = RenderMode.WholeSite;
    public List<SelectorCascade> Selectors { get; set; } = [];
    public List<StickerBinding> StickerBindings { get; set; } = [];
    public List<ThresholdRule> ThresholdRules { get; set; } = [];
    public string? ThemeOverride { get; set; }
    public int RefreshHintMs { get; set; } = 1000;
    public bool IsLibraryItem { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class StickerBinding
{
    public int SelectorIndex { get; set; }
    public Guid StickerId { get; set; }
}

public enum ComponentType
{
    WholeSite,
    Crop,
    Clone
}

public enum RenderMode
{
    WholeSite,
    Crop,
    Clone
}

public interface IComponentRepository
{
    Task<IReadOnlyList<Component>> GetAllAsync();
    Task<IReadOnlyList<Component>> GetLibraryItemsAsync();
    Task<Component?> GetByIdAsync(Guid id);
    Task SaveAsync(Component component);
    Task DeleteAsync(Guid id);
}
