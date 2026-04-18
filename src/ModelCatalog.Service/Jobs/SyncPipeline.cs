using ModelCatalog.Client.Dtos;
using ModelCatalog.Service.Catalog;
using ModelCatalog.Service.Merging;
using ModelCatalog.Service.Metrics;
using ModelCatalog.Service.Sources;
using Prometheus;

namespace ModelCatalog.Service.Jobs;

public sealed class SyncPipeline(
    IEnumerable<ISource> sources,
    PriorityMerger merger,
    SnapshotStore store,
    TimeProvider clock,
    ILogger<SyncPipeline> logger
)
{
    private static readonly Action<ILogger, string, Exception?> LogSourceFailed =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(1, "SourceFailed"),
            "Source {Source} fetch failed"
        );

    private static readonly Action<ILogger, int, int, int, Exception?> LogSyncComplete =
        LoggerMessage.Define<int, int, int>(
            LogLevel.Information,
            new EventId(2, "SyncComplete"),
            "Sync complete: {ModelCount} models, {SuccessCount}/{TotalCount} sources"
        );

    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = MetricsRegistry.RefreshDuration.NewTimer();

        var now = clock.GetUtcNow();
        var sourceList = sources.ToList();
        var prevStates =
            store.Current?.SourceStates.ToDictionary(
                s => s.Source,
                StringComparer.OrdinalIgnoreCase
            ) ?? new Dictionary<string, SourceState>(StringComparer.OrdinalIgnoreCase);

        var results = await Task.WhenAll(
                sourceList.Select(s => FetchOneAsync(s, now, prevStates, ct))
            )
            .ConfigureAwait(false);
        var liveSnaps = results.Where(r => r.Snap is not null).Select(r => r.Snap!).ToList();
        var states = results.Select(r => r.State).ToList();

        var merged =
            liveSnaps.Count > 0
                ? merger.Merge(liveSnaps)
                : store.Current?.Models ?? Array.Empty<ModelInfo>();

        var snapshot = new NormalizedSnapshot(
            FetchedAt: liveSnaps.Count > 0 ? now : store.Current?.FetchedAt ?? now,
            Models: merged,
            SourceStates: states,
            RawSources: liveSnaps
        );

        await store.SwapAsync(snapshot, ct).ConfigureAwait(false);

        foreach (var r in results)
        {
            if (r.Snap is null)
                MetricsRegistry.RefreshErrors.WithLabels(r.Name).Inc();
            if (r.State.LastSuccess is { } ls)
                MetricsRegistry
                    .SourceLastSuccessSeconds.WithLabels(r.Name)
                    .Set((clock.GetUtcNow() - ls).TotalSeconds);
        }
        MetricsRegistry.ModelsTotal.Set(merged.Count);

        LogSyncComplete(logger, merged.Count, liveSnaps.Count, sourceList.Count, null);
    }

    private async Task<(string Name, SourceSnapshot? Snap, SourceState State)> FetchOneAsync(
        ISource s,
        DateTimeOffset now,
        Dictionary<string, SourceState> prevStates,
        CancellationToken ct
    )
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var snap = await s.FetchAsync(cts.Token).ConfigureAwait(false);
            return (s.Name, Snap: (SourceSnapshot?)snap, State: new SourceState(s.Name, now, null));
        }
#pragma warning disable CA1031
        catch (Exception ex)
            when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            LogSourceFailed(logger, s.Name, ex);
            var prev = prevStates.TryGetValue(s.Name, out var p) ? p.LastSuccess : null;
            return (
                s.Name,
                Snap: (SourceSnapshot?)null,
                State: new SourceState(s.Name, prev, ex.Message)
            );
        }
#pragma warning restore CA1031
    }
}
