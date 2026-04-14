using ModelCatalog.Client.Dtos;

namespace ModelCatalog.Service.Sources;

public sealed record SourceSnapshot(
    string SourceName,
    DateTimeOffset FetchedAt,
    IReadOnlyList<ModelInfo> Models);
