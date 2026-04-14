using Clarive.ModelRegistry.Service.Jobs;

namespace Clarive.ModelRegistry.Service.Endpoints;

public static class RefreshEndpoints
{
    public static RouteGroupBuilder MapRefreshEndpoints(this RouteGroupBuilder g, SyncPipeline pipeline)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(pipeline);

        g.MapPost("/refresh", () =>
        {
            if (!SyncJob.TryBeginRun()) return Results.Conflict("Refresh already running");
            _ = Task.Run(async () =>
            {
                try { await pipeline.RunAsync(CancellationToken.None).ConfigureAwait(false); }
                finally { SyncJob.EndRun(); }
            });
            return Results.Accepted();
        });
        return g;
    }
}
