namespace ModelCatalog.Client.Dtos;

public sealed record ModelInfo(
    string Id,
    string Provider,
    string ModelId,
    string? DisplayName,
    Pricing? Pricing,
    Context? Context,
    Capabilities Capabilities,
    Modality Modality,
    IReadOnlyList<string> Sources,
    DateTimeOffset LastUpdated
);
