using Prometheus;

namespace ModelCatalog.Service.Metrics;

public static class MetricsRegistry
{
    private static readonly string[] SourceLabel = ["source"];

    // architecture-guideline.md §7 names both of these exactly, and the estate's
    // `feed-stale-daily` alert selects on the `feed` label by name. Two metrics rather than one
    // so the alert is the same shape for every feed and never has to know a cadence:
    // `time() - last_success > expected_interval * 1.5`.
    private static readonly string[] FeedLabel = ["feed"];

    public static readonly Gauge ModelsTotal = Prometheus.Metrics.CreateGauge(
        "model_registry_models_total",
        "Total models in the current snapshot"
    );

    /// <summary>
    /// When each feed last fetched successfully, as a Unix timestamp — not seconds-since, which
    /// is what this used to publish under a name no alert in this estate looks for.
    ///
    /// A feed that has never succeeded publishes no value, not zero (§7): zero is 1970, which
    /// reads as silent forever. That holds structurally because the labelled child is only
    /// created on the first successful write in <see cref="Jobs.SyncPipeline"/> — moving that
    /// write out of its success test would quietly reintroduce the 1970 timestamp.
    /// </summary>
    public static readonly Gauge FeedLastSuccessTimestampSeconds = Prometheus.Metrics.CreateGauge(
        "feed_last_success_timestamp_seconds",
        "When this feed last fetched successfully, as a Unix timestamp",
        new GaugeConfiguration { LabelNames = FeedLabel }
    );

    /// <summary>
    /// How often each feed is supposed to fetch. Derived from the configured sync cron rather
    /// than hardcoded, so it cannot contradict `ModelRegistry:SyncCron`.
    /// </summary>
    public static readonly Gauge FeedExpectedIntervalSeconds = Prometheus.Metrics.CreateGauge(
        "feed_expected_interval_seconds",
        "How often this feed is supposed to fetch, in seconds",
        new GaugeConfiguration { LabelNames = FeedLabel }
    );

    public static readonly Histogram RefreshDuration = Prometheus.Metrics.CreateHistogram(
        "model_registry_refresh_duration_seconds",
        "Duration of a full refresh cycle"
    );

    public static readonly Counter RefreshErrors = Prometheus.Metrics.CreateCounter(
        "model_registry_refresh_errors_total",
        "Refresh error count per source",
        new CounterConfiguration { LabelNames = SourceLabel }
    );
}
