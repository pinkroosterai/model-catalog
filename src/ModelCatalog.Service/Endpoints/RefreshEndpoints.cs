using Microsoft.Extensions.Options;
using ModelCatalog.Service.Auth;
using ModelCatalog.Service.Jobs;

namespace ModelCatalog.Service.Endpoints;

public static class RefreshEndpoints
{
    public static RouteGroupBuilder MapRefreshEndpoints(
        this RouteGroupBuilder g,
        SyncPipeline pipeline,
        IOptionsMonitor<ApiKeyOptions> apiKeyOptions
    )
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(apiKeyOptions);

        g.MapPost(
            "/refresh",
            (HttpContext ctx) =>
            {
                var configured = apiKeyOptions.CurrentValue.ApiKeys;
                if (configured.Count == 0)
                    return Results.Problem(
                        statusCode: 503,
                        detail: "Refresh disabled — no api keys configured"
                    );

                if (
                    !ctx.Request.Headers.TryGetValue("X-Api-Key", out var provided)
                    || !configured.Any(k =>
                        string.Equals(k.Key, provided.ToString(), StringComparison.Ordinal)
                    )
                )
                {
                    return Results.Unauthorized();
                }

                if (!SyncJob.TryBeginRun())
                    return Results.Conflict("Refresh already running");
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await pipeline.RunAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                    finally
                    {
                        SyncJob.EndRun();
                    }
                });
                return Results.Accepted();
            }
        );
        return g;
    }
}
