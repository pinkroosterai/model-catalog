using Clarive.ModelRegistry.Service.Sources;

namespace Clarive.ModelRegistry.Service.IntegrationTests.Fakes;

public sealed class FakeSource(string name, Func<CancellationToken, Task<SourceSnapshot>> fetch) : ISource
{
    public string Name => name;
    public Task<SourceSnapshot> FetchAsync(CancellationToken ct) => fetch(ct);
}
