namespace ModelCatalog.Client.Dtos;

public sealed record CatalogMeta(
    DateTimeOffset SnapshotAt,
    TimeSpan Staleness,
    IReadOnlyList<SourceState> SourceStates,
    bool Healthy);

public sealed record SourceState(
    string Source,
    DateTimeOffset? LastSuccess,
    string? LastError);
