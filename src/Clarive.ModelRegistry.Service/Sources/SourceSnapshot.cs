using Clarive.ModelRegistry.Client.Dtos;

namespace Clarive.ModelRegistry.Service.Sources;

public sealed record SourceSnapshot(
    string SourceName,
    DateTimeOffset FetchedAt,
    IReadOnlyList<ModelInfo> Models);
