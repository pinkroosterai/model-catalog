using Clarive.ModelRegistry.Client.Dtos;
using Clarive.ModelRegistry.Service.Sources;

namespace Clarive.ModelRegistry.Service.Catalog;

public sealed record NormalizedSnapshot(
    DateTimeOffset FetchedAt,
    IReadOnlyList<ModelInfo> Models,
    IReadOnlyList<SourceState> SourceStates,
    IReadOnlyList<SourceSnapshot> RawSources);
