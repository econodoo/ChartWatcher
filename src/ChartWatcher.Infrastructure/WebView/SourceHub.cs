using ChartWatcher.Application.Services;
using ChartWatcher.Core.Sources;
using Serilog;

namespace ChartWatcher.Infrastructure.WebView;

/// <summary>
/// Manages source lifecycle, status tracking, and mutation event routing.
/// Does NOT create WebView2 controls — the UI layer handles that and
/// reports status/mutations back via ReportReady/ReportMutation/ReportError.
/// </summary>
public sealed class SourceHub : ISourceHub
{
    private readonly Dictionary<Guid, SourceState> _states = [];
    private readonly ILogger _log;

    public SourceHub(ILogger log)
    {
        _log = log;
    }

    public event EventHandler<SourceStatusChangedArgs>? StatusChanged;
    public event EventHandler<MutationReceivedArgs>? MutationReceived;

    public IReadOnlyList<Guid> ActiveSourceIds => _states.Keys.ToList();

    public Task<bool> StartSourceAsync(Source source)
    {
        if (_states.ContainsKey(source.Id))
        {
            _log.Information("Source {Name} already registered", source.Name);
            return Task.FromResult(true);
        }

        _states[source.Id] = new SourceState
        {
            Source = source,
            Status = SourceStatus.Loading,
            StartedAt = DateTime.UtcNow
        };

        RaiseStatus(source.Id, SourceStatus.Loading, $"Starting {source.Name}...");
        _log.Information("Source {Name} registered", source.Name);
        return Task.FromResult(true);
    }

    public Task StopSourceAsync(Guid sourceId)
    {
        if (_states.Remove(sourceId))
        {
            RaiseStatus(sourceId, SourceStatus.Idle, "Stopped");
            _log.Information("Source {Id} stopped", sourceId);
        }
        return Task.CompletedTask;
    }

    public Task ReloadSourceAsync(Guid sourceId)
    {
        if (_states.TryGetValue(sourceId, out var state))
        {
            state.Status = SourceStatus.Loading;
            state.LastReload = DateTime.UtcNow;
            RaiseStatus(sourceId, SourceStatus.Loading, "Reloading...");
        }
        return Task.CompletedTask;
    }

    public SourceStatus GetStatus(Guid sourceId) =>
        _states.TryGetValue(sourceId, out var s) ? s.Status : SourceStatus.Idle;

    // ── Called by UI layer ──

    public void ReportReady(Guid sourceId)
    {
        UpdateState(sourceId, SourceStatus.Ready);
        RaiseStatus(sourceId, SourceStatus.Ready, "Connected");
    }

    public void ReportMutation(Guid sourceId, string stickerId, string html)
    {
        if (_states.TryGetValue(sourceId, out var state))
        {
            state.LastMutation = DateTime.UtcNow;
            state.MutationCount++;
            if (state.Status != SourceStatus.Ready)
                UpdateState(sourceId, SourceStatus.Ready);
        }

        MutationReceived?.Invoke(this, new MutationReceivedArgs
        {
            SourceId = sourceId,
            StickerId = stickerId,
            Html = html,
            Timestamp = DateTime.UtcNow
        });
    }

    public void ReportError(Guid sourceId, string message)
    {
        UpdateState(sourceId, SourceStatus.Error);
        RaiseStatus(sourceId, SourceStatus.Error, message);
        _log.Error("Source {Id} error: {Msg}", sourceId, message);
    }

    // ── Internals ──

    private void UpdateState(Guid sourceId, SourceStatus status)
    {
        if (_states.TryGetValue(sourceId, out var state))
        {
            state.Status = status;
            if (status == SourceStatus.Ready)
                state.LastMutation = DateTime.UtcNow;
        }
    }

    private void RaiseStatus(Guid sourceId, SourceStatus status, string? message)
    {
        StatusChanged?.Invoke(this, new SourceStatusChangedArgs
        {
            SourceId = sourceId,
            Status = status,
            Message = message
        });
    }

    private sealed class SourceState
    {
        public required Source Source { get; init; }
        public SourceStatus Status { get; set; }
        public DateTime StartedAt { get; init; }
        public DateTime? LastMutation { get; set; }
        public DateTime? LastReload { get; set; }
        public long MutationCount { get; set; }
    }
}
