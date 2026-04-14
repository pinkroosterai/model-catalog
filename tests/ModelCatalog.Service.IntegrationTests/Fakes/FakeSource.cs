using ModelCatalog.Service.Sources;

namespace ModelCatalog.Service.IntegrationTests.Fakes;

public sealed class FakeSource(string name, Func<CancellationToken, Task<SourceSnapshot>> fetch) : ISource
{
    public string Name => name;
    public Task<SourceSnapshot> FetchAsync(CancellationToken ct) => fetch(ct);
}
