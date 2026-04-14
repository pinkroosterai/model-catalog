using ModelCatalog.Service.Catalog;

namespace ModelCatalog.Service.Endpoints;

public static class SourceEndpoints
{
    public static RouteGroupBuilder MapSourceEndpoints(this RouteGroupBuilder g, SnapshotStore store)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(store);

        g.MapGet("/sources", () =>
        {
            if (store.Current is null) return Results.Problem(statusCode: 503);
            return Results.Ok(store.Current.RawSources.Select(s => new { s.SourceName, s.FetchedAt, ModelCount = s.Models.Count }));
        });

        g.MapGet("/sources/{source}/models/{provider}/{modelId}", (string source, string provider, string modelId) =>
        {
            if (store.Current is null) return Results.Problem(statusCode: 503);
            var raw = store.Current.RawSources.FirstOrDefault(s =>
                string.Equals(s.SourceName, source, StringComparison.OrdinalIgnoreCase));
            if (raw is null) return Results.NotFound();
#pragma warning disable CA1308
            var id = $"{provider}/{modelId}".ToLowerInvariant();
#pragma warning restore CA1308
            var m = raw.Models.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.Ordinal));
            return m is null ? Results.NotFound() : Results.Ok(m);
        });

        return g;
    }
}
