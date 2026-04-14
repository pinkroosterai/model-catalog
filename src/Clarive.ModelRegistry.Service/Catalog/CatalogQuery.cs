using Clarive.ModelRegistry.Client.Dtos;

namespace Clarive.ModelRegistry.Service.Catalog;

public static class CatalogQuery
{
    public static IEnumerable<ModelInfo> Filter(IEnumerable<ModelInfo> models, ModelQuery q) =>
        models.Where(m =>
            (q.Provider is null || string.Equals(m.Provider, q.Provider, StringComparison.OrdinalIgnoreCase))
            && (q.Modality is null || m.Modality == q.Modality)
            && (q.IsReasoning is null || m.Capabilities.IsReasoning == q.IsReasoning)
            && (q.SupportsFunctionCalling is null
                || m.Capabilities.SupportsFunctionCalling == q.SupportsFunctionCalling));
}
