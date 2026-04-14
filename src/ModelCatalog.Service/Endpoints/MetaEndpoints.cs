using ModelCatalog.Client.Dtos;
using ModelCatalog.Service.Catalog;

namespace ModelCatalog.Service.Endpoints;

public static class MetaEndpoints
{
    public static RouteGroupBuilder MapMetaEndpoints(this RouteGroupBuilder g,
        SnapshotStore store, TimeProvider clock, TimeSpan staleThreshold)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);

        g.MapGet("/meta", () =>
        {
            var snap = store.Current;
            if (snap is null)
                return Results.Ok(new CatalogMeta(
                    DateTimeOffset.MinValue, TimeSpan.Zero, Array.Empty<SourceState>(), false));
            var staleness = clock.GetUtcNow() - snap.FetchedAt;
            return Results.Ok(new CatalogMeta(
                snap.FetchedAt, staleness, snap.SourceStates, staleness < staleThreshold));
        });
        return g;
    }
}
