using ModelCatalog.Client.Dtos;
using ModelCatalog.Service.Catalog;

namespace ModelCatalog.Service.Endpoints;

public static class ModelEndpoints
{
    public static RouteGroupBuilder MapModelEndpoints(this RouteGroupBuilder g, SnapshotStore store)
    {
        ArgumentNullException.ThrowIfNull(g);
        ArgumentNullException.ThrowIfNull(store);

        g.MapGet(
            "/models",
            (
                string? provider,
                Modality? modality,
                bool? isReasoning,
                bool? supportsFunctionCalling
            ) =>
            {
                if (store.Current is null)
                    return Results.Problem(statusCode: 503, detail: "Snapshot not yet available");
                var q = new ModelQuery(provider, modality, isReasoning, supportsFunctionCalling);
                return Results.Ok(CatalogQuery.Filter(store.Current.Models, q));
            }
        );

        g.MapGet(
            "/models/{provider}/{modelId}",
            (string provider, string modelId) =>
            {
                if (store.Current is null)
                    return Results.Problem(statusCode: 503, detail: "Snapshot not yet available");
#pragma warning disable CA1308
                var id = $"{provider}/{modelId}".ToLowerInvariant();
#pragma warning restore CA1308
                var m = store.Current.Models.FirstOrDefault(x =>
                    string.Equals(x.Id, id, StringComparison.Ordinal)
                );
                return m is null ? Results.NotFound() : Results.Ok(m);
            }
        );

        return g;
    }
}
