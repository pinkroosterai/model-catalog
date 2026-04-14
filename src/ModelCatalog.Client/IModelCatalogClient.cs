using ModelCatalog.Client.Dtos;

namespace ModelCatalog.Client;

public interface IModelCatalogClient
{
    Task<ModelInfo?> GetModelAsync(string canonicalId, CancellationToken ct = default);
    Task<ModelInfo?> GetModelAsync(string provider, string modelId, CancellationToken ct = default);
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(ModelQuery? query = null, CancellationToken ct = default);
    Task<CatalogMeta> GetMetaAsync(CancellationToken ct = default);
}
