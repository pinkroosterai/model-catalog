#pragma warning disable CA1515, S1118, S6966, MA0004, CA1031, CA2000, CA1305, CA1849
using Clarive.ModelRegistry.Service.Aliases;
using Clarive.ModelRegistry.Service.Auth;
using Clarive.ModelRegistry.Service.Catalog;
using Clarive.ModelRegistry.Service.Endpoints;
using Clarive.ModelRegistry.Service.Jobs;
using Clarive.ModelRegistry.Service.Merging;
using Clarive.ModelRegistry.Service.Sources;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;
using Prometheus;
using Quartz;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();
builder.Host.UseSerilog();

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
var breaker = HttpPolicyExtensions.HandleTransientHttpError()
    .CircuitBreakerAsync(5, TimeSpan.FromMinutes(5));

builder.Services.AddHttpClient<LiteLlmSource>(c =>
    c.BaseAddress = new Uri(cfg["ModelRegistry:Sources:LiteLlm:Url"]
        ?? "https://raw.githubusercontent.com/BerriAI/litellm/main/model_prices_and_context_window.json"))
    .AddPolicyHandler(resiliencePolicy).AddPolicyHandler(breaker);

builder.Services.AddHttpClient<OpenRouterSource>(c =>
    c.BaseAddress = new Uri(cfg["ModelRegistry:Sources:OpenRouter:Url"] ?? "https://openrouter.ai/api/v1/"))
    .AddPolicyHandler(resiliencePolicy).AddPolicyHandler(breaker);

builder.Services.AddHttpClient<ModelsDevSource>(c =>
    c.BaseAddress = new Uri(cfg["ModelRegistry:Sources:ModelsDev:Url"] ?? "https://models.dev/"))
    .AddPolicyHandler(resiliencePolicy).AddPolicyHandler(breaker);

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
    q.AddTrigger(t => t.ForJob(jobKey).WithIdentity("sync-daily")
        .WithCronSchedule(cfg["ModelRegistry:SyncCron"] ?? "0 0 1 * * ?"));
});
builder.Services.AddQuartzHostedService(opt => opt.WaitForJobsToComplete = true);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

await app.Services.GetRequiredService<SnapshotStore>().TryLoadFromDiskAsync(CancellationToken.None);

app.UseMiddleware<ApiKeyMiddleware>();
app.MapOpenApi();
app.UseHttpMetrics();
app.MapMetrics();

var v1 = app.MapGroup("/v1");
v1.MapModelEndpoints(app.Services.GetRequiredService<SnapshotStore>());
v1.MapSourceEndpoints(app.Services.GetRequiredService<SnapshotStore>());
v1.MapMetaEndpoints(
    app.Services.GetRequiredService<SnapshotStore>(),
    app.Services.GetRequiredService<TimeProvider>(),
    TimeSpan.FromHours(staleHours));
v1.MapRefreshEndpoints(app.Services.GetRequiredService<SyncPipeline>());

app.MapGet("/healthz", (SnapshotStore store, TimeProvider clock) =>
{
    var snap = store.Current;
    if (snap is null) return Results.Problem(statusCode: 503, detail: "Snapshot unavailable");
    var age = clock.GetUtcNow() - snap.FetchedAt;
    return age < TimeSpan.FromHours(staleHours)
        ? Results.Ok(new { status = "healthy", snapshotAgeHours = age.TotalHours })
        : Results.Json(new { status = "degraded", snapshotAgeHours = age.TotalHours }, statusCode: 503);
});

app.Run();

public partial class Program;
#pragma warning restore CA1515, S1118, S6966, MA0004, CA1031, CA2000, CA1305, CA1849
