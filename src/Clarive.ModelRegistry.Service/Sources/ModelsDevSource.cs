namespace Clarive.ModelRegistry.Service.Sources;

public sealed class ModelsDevSource(HttpClient http, ModelsDevNormalizer normalizer, TimeProvider clock)
    : ISource
{
    private static readonly Uri ApiPath = new("api.json", UriKind.Relative);

    public string Name => "modelsdev";

    public async Task<SourceSnapshot> FetchAsync(CancellationToken ct)
    {
        var raw = await http.GetStringAsync(ApiPath, ct).ConfigureAwait(false);
        return normalizer.Normalize(raw, clock.GetUtcNow());
    }
}
