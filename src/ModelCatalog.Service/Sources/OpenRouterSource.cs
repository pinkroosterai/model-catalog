namespace ModelCatalog.Service.Sources;

public sealed class OpenRouterSource(HttpClient http, OpenRouterNormalizer normalizer, TimeProvider clock)
    : ISource
{
    private static readonly Uri ModelsPath = new("models", UriKind.Relative);

    public string Name => "openrouter";

    public async Task<SourceSnapshot> FetchAsync(CancellationToken ct)
    {
        var raw = await http.GetStringAsync(ModelsPath, ct).ConfigureAwait(false);
        return normalizer.Normalize(raw, clock.GetUtcNow());
    }
}
