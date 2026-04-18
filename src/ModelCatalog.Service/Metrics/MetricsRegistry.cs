using Prometheus;

namespace ModelCatalog.Service.Metrics;

public static class MetricsRegistry
{
    private static readonly string[] SourceLabel = ["source"];

    public static readonly Gauge ModelsTotal = Prometheus.Metrics.CreateGauge(
        "model_registry_models_total",
        "Total models in the current snapshot"
    );

    public static readonly Gauge SourceLastSuccessSeconds = Prometheus.Metrics.CreateGauge(
        "model_registry_source_last_success_seconds",
        "Seconds since last successful fetch per source",
        new GaugeConfiguration { LabelNames = SourceLabel }
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
