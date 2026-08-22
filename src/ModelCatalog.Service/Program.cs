#pragma warning disable CA1515, S1118, S6966, MA0004, CA1031, CA2000, CA1305, CA1849
using Microsoft.Extensions.Options;
using ModelCatalog.Service.Aliases;
using ModelCatalog.Service.Auth;
using ModelCatalog.Service.Catalog;
using ModelCatalog.Service.Endpoints;
using ModelCatalog.Service.Jobs;
using ModelCatalog.Service.Merging;
using ModelCatalog.Service.Metrics;
using ModelCatalog.Service.Observability;
using ModelCatalog.Service.Sources;
using Polly;
using Polly.Extensions.Http;
using Prometheus;
using Quartz;

// The container healthcheck, before anything is built — see HealthProbe.
if (args.Contains("--health", StringComparer.Ordinal))
    return await HealthProbe.RunAsync().ConfigureAwait(false);

var builder = WebApplication.CreateBuilder(args);

// architecture-guideline.md §7: JSON to stdout, five named fields, copied per service rather
// than imported. Replaces Serilog, which was only ever configured as a plain console sink.
builder.Logging.AddEstateJsonLogging(builder.Configuration, "modelcatalog");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.Configure<ApiKeyOptions>(builder.Configuration.GetSection("ModelRegistry"));
builder.Services.Configure<MergeOptions>(builder.Configuration.GetSection("ModelRegistry:Merge"));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<MergeOptions>>().Value);

var cfg = builder.Configuration;
var snapshotPath = cfg["ModelRegistry:SnapshotPath"] ?? "data/snapshot.json";
var staleHours = cfg.GetValue("ModelRegistry:StaleThresholdHours", 72);
var aliasMapPath = cfg["ModelRegistry:AliasMapPath"] ?? "Aliases/alias-map.json";

builder.Services.AddSingleton(new SnapshotStore(snapshotPath));
builder.Services.AddSingleton(_ => AliasResolver.LoadFromFile(aliasMapPath));
builder.Services.AddSingleton<LiteLlmNormalizer>();
builder.Services.AddSingleton<OpenRouterNormalizer>();
builder.Services.AddSingleton<ModelsDevNormalizer>();
builder.Services.AddSingleton<PriorityMerger>();
builder.Services.AddSingleton<SyncPipeline>();

var resiliencePolicy = HttpPolicyExtensions
    .HandleTransientHttpError()
    .WaitAndRetryAsync(3, a => TimeSpan.FromSeconds(Math.Pow(4, a - 1)));
var breaker = HttpPolicyExtensions
    .HandleTransientHttpError()
    .CircuitBreakerAsync(5, TimeSpan.FromMinutes(5));

builder
    .Services.AddHttpClient<LiteLlmSource>(c =>
        c.BaseAddress = new Uri(
            cfg["ModelRegistry:Sources:LiteLlm:Url"]
                ?? "https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json"
        )
    )
    .AddPolicyHandler(resiliencePolicy)
    .AddPolicyHandler(breaker);

builder
    .Services.AddHttpClient<OpenRouterSource>(c =>
        c.BaseAddress = new Uri(
            cfg["ModelRegistry:Sources:OpenRouter:Url"] ?? "https://openrouter.ai/api/v1/"
        )
    )
    .AddPolicyHandler(resiliencePolicy)
    .AddPolicyHandler(breaker);

builder
    .Services.AddHttpClient<ModelsDevSource>(c =>
        c.BaseAddress = new Uri(cfg["ModelRegistry:Sources:ModelsDev:Url"] ?? "https://models.dev/")
    )
    .AddPolicyHandler(resiliencePolicy)
    .AddPolicyHandler(breaker);

builder.Services.AddSingleton<ISource>(sp => sp.GetRequiredService<LiteLlmSource>());
builder.Services.AddSingleton<ISource>(sp => sp.GetRequiredService<OpenRouterSource>());
builder.Services.AddSingleton<ISource>(sp => sp.GetRequiredService<ModelsDevSource>());

builder.Services.AddQuartz(q =>
{
    var jobKey = new JobKey("sync");
    q.AddJob<SyncJob>(o => o.WithIdentity(jobKey));
    if (cfg.GetValue("ModelRegistry:RunSyncOnStartup", true))
    {
        q.AddTrigger(t => t.ForJob(jobKey).WithIdentity("sync-startup").StartNow());
    }
    q.AddTrigger(t =>
        t.ForJob(jobKey)
            .WithIdentity("sync-daily")
            .WithCronSchedule(cfg["ModelRegistry:SyncCron"] ?? "0 0 1 * * ?")
    );
});
builder.Services.AddQuartzHostedService(opt => opt.WaitForJobsToComplete = true);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

await app.Services.GetRequiredService<SnapshotStore>().TryLoadFromDiskAsync(CancellationToken.None);

// §7's second feed metric. Published for every feed at startup, before any of them has run:
// it states the contract, where feed_last_success_timestamp_seconds states the fact.
var syncCron = cfg["ModelRegistry:SyncCron"] ?? "0 0 1 * * ?";
if (CronInterval.SecondsBetweenRuns(syncCron, DateTimeOffset.UtcNow) is { } intervalSeconds)
{
    foreach (var source in app.Services.GetServices<ISource>())
        MetricsRegistry.FeedExpectedIntervalSeconds.WithLabels(source.Name).Set(intervalSeconds);
}

app.MapOpenApi();
app.UseMiddleware<RequestIdMiddleware>();
app.UseHttpMetrics();
app.MapMetrics();

var v1 = app.MapGroup("/v1");
v1.MapModelEndpoints(app.Services.GetRequiredService<SnapshotStore>());
v1.MapSourceEndpoints(app.Services.GetRequiredService<SnapshotStore>());
v1.MapMetaEndpoints(
    app.Services.GetRequiredService<SnapshotStore>(),
    app.Services.GetRequiredService<TimeProvider>(),
    TimeSpan.FromHours(staleHours)
);
v1.MapRefreshEndpoints(
    app.Services.GetRequiredService<SyncPipeline>(),
    app.Services.GetRequiredService<IOptionsMonitor<ApiKeyOptions>>()
);

// The estate's endpoint (architecture-guideline.md §6): reports its dependencies by name rather
// than a bare 200, and answers 503 only when there is no snapshot to serve at all. Staleness is
// reported in the body and is `feed-stale-daily`'s job to alert on, not this endpoint's to fail.
app.MapGet(
    "/health",
    (SnapshotStore store, TimeProvider clock) =>
    {
        var snap = store.Current;
        if (snap is null)
            return Results.Problem(statusCode: 503, detail: "Snapshot unavailable");
        var age = clock.GetUtcNow() - snap.FetchedAt;
        var stale = age >= TimeSpan.FromHours(staleHours);
        return Results.Ok(
            new
            {
                status = stale ? "degraded" : "healthy",
                snapshotAgeHours = age.TotalHours,
                stale,
                feeds = snap.SourceStates.Select(s => new
                {
                    feed = s.Source,
                    lastSuccess = s.LastSuccess,
                    lastError = s.LastError,
                }),
            }
        );
    }
);

// Kept as it was, and kept deliberately: this is a documented endpoint on an image already
// published to ghcr, so a self-hoster's healthcheck may be pointing at it. Its 503-when-stale
// answers a different question from /health above — "should you trust this data", not "is this
// container serving".
app.MapGet(
    "/healthz",
    (SnapshotStore store, TimeProvider clock) =>
    {
        var snap = store.Current;
        if (snap is null)
            return Results.Problem(statusCode: 503, detail: "Snapshot unavailable");
        var age = clock.GetUtcNow() - snap.FetchedAt;
        return age < TimeSpan.FromHours(staleHours)
            ? Results.Ok(new { status = "healthy", snapshotAgeHours = age.TotalHours })
            : Results.Json(
                new { status = "degraded", snapshotAgeHours = age.TotalHours },
                statusCode: 503
            );
    }
);

app.Run();

return 0;

public partial class Program;
#pragma warning restore CA1515, S1118, S6966, MA0004, CA1031, CA2000, CA1305, CA1849
