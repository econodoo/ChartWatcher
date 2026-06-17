using ChartWatcher.Core.Sources;

namespace ChartWatcher.Application.Services;

/// <summary>
/// Manages the pool of hidden WebView2 instances, one per active Source.
/// </summary>
public interface ISourceHub
{
    event EventHandler<SourceStatusChangedArgs>? StatusChanged;
    event EventHandler<MutationReceivedArgs>? MutationReceived;

    Task<bool> StartSourceAsync(Source source);
    Task StopSourceAsync(Guid sourceId);
    Task ReloadSourceAsync(Guid sourceId);
    SourceStatus GetStatus(Guid sourceId);
    IReadOnlyList<Guid> ActiveSourceIds { get; }

    // Called by UI layer to report WebView2 status
    void ReportReady(Guid sourceId);
    void ReportMutation(Guid sourceId, string stickerId, string html);
    void ReportError(Guid sourceId, string message);
}

public sealed class SourceStatusChangedArgs : EventArgs
{
    public Guid SourceId { get; init; }
    public SourceStatus Status { get; init; }
    public string? Message { get; init; }
}

public sealed class MutationReceivedArgs : EventArgs
{
    public Guid SourceId { get; init; }
    public string StickerId { get; init; } = string.Empty;
    public string Html { get; init; } = string.Empty;
    public DateTime Timestamp { get; init; }
}

public enum SourceStatus
{
    Idle,
    Loading,
    Ready,
    Stale,
    Error
}
