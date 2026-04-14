namespace Clarive.ModelRegistry.Service.Sources;

public interface ISource
{
    string Name { get; }
    Task<SourceSnapshot> FetchAsync(CancellationToken ct);
}
