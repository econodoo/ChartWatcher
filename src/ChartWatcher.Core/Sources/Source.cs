namespace ChartWatcher.Core.Sources;

public sealed class Source
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string EntryUrl { get; set; } = string.Empty;
    public string ProviderId { get; set; } = "custom";
    public LoginCapability LoginCapability { get; set; } = LoginCapability.Anonymous;
    public string UserDataFolder { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUsedAt { get; set; } = DateTime.UtcNow;
}

public enum LoginCapability
{
    Anonymous,
    Interactive
}

public interface ISourceRepository
{
    Task<IReadOnlyList<Source>> GetAllAsync();
    Task<Source?> GetByIdAsync(Guid id);
    Task SaveAsync(Source source);
    Task DeleteAsync(Guid id);
}
