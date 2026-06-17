using System.Text.Json;

namespace ChartWatcher.Application.Services;

/// <summary>
/// .NET → JS command (bridge protocol)
/// </summary>
public sealed class BridgeCommand
{
    public string Cmd { get; set; } = string.Empty;
    public string? StickerId { get; set; }
    public object? Cascade { get; set; }
    public object? Cascades { get; set; }
    public object? Slots { get; set; }
}

/// <summary>
/// JS → .NET event (bridge protocol)
/// </summary>
public sealed class BridgeMessage
{
    public Guid SourceId { get; set; }
    public string Evt { get; set; } = string.Empty;
    public string? StickerId { get; set; }
    public string? Html { get; set; }
    public long? Ts { get; set; }
    public string? InnerText { get; set; }
    public string? Reason { get; set; }
    public JsonElement? Cascade { get; set; }
    public JsonElement? Attrs { get; set; }
    public JsonElement? Rect { get; set; }
}
