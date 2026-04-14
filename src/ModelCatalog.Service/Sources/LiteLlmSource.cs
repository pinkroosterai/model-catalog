namespace ModelCatalog.Service.Sources;

public sealed class LiteLlmSource(HttpClient http, LiteLlmNormalizer normalizer, TimeProvider clock)
    : ISource
{
    public string Name => "litellm";

    public async Task<SourceSnapshot> FetchAsync(CancellationToken ct)
    {
        var raw = await http.GetStringAsync((Uri?)null, ct).ConfigureAwait(false);
        return normalizer.Normalize(raw, clock.GetUtcNow());
    }
}
