using ModelCatalog.Client.Dtos;
using ModelCatalog.Service.Sources;

namespace ModelCatalog.Service.Catalog;

public sealed record NormalizedSnapshot(
    DateTimeOffset FetchedAt,
    IReadOnlyList<ModelInfo> Models,
    IReadOnlyList<SourceState> SourceStates,
    IReadOnlyList<SourceSnapshot> RawSources);
