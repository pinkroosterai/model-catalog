using System.Globalization;
using System.Text.Json;
using ModelCatalog.Client.Dtos;

namespace ModelCatalog.Service.Sources;

public sealed class OpenRouterNormalizer
{
    private const decimal PerTokenToPerMillion = 1_000_000m;
    private static readonly string[] UnknownFallback = ["unknown"];
    private static readonly string[] SourceLabel = ["openrouter"];

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822",
        Justification = "Instance method for DI parity with other sources"
    )]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Minor Code Smell",
        "S2325",
        Justification = "Instance method for DI parity with other sources"
    )]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Globalization",
        "CA1308",
        Justification = "Canonical ids are lowercase"
    )]
    public SourceSnapshot Normalize(string rawJson, DateTimeOffset fetchedAt)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var models = new List<ModelInfo>();

        if (
            !doc.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Array
        )
            return new SourceSnapshot("openrouter", fetchedAt, models);

        foreach (var el in data.EnumerateArray())
        {
            var id = el.GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(id))
                continue;

            var parts = id.Contains('/', StringComparison.Ordinal)
                ? id.Split('/', 2)
                : [.. UnknownFallback, id];

            var supported =
                el.TryGetProperty("supported_parameters", out var sp)
                && sp.ValueKind == JsonValueKind.Array
                    ? sp.EnumerateArray()
                        .Select(x => x.GetString() ?? string.Empty)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var inputModalities =
                el.TryGetProperty("architecture", out var arch)
                && arch.TryGetProperty("input_modalities", out var im)
                && im.ValueKind == JsonValueKind.Array
                    ? im.EnumerateArray()
                        .Select(x => x.GetString() ?? string.Empty)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            models.Add(
                new ModelInfo(
                    Id: id.ToLowerInvariant(),
                    Provider: parts[0].ToLowerInvariant(),
                    ModelId: parts[1],
                    DisplayName: el.TryGetProperty("name", out var n) ? n.GetString() : null,
                    Pricing: ExtractPricing(el),
                    Context: ExtractContext(el),
                    Capabilities: new Capabilities(
                        IsReasoning: supported.Contains("reasoning") ? true : null,
                        SupportsFunctionCalling: supported.Contains("tools") ? true : null,
                        SupportsResponseSchema: supported.Contains("response_format") ? true : null,
                        SupportsVision: inputModalities.Contains("image") ? true : null,
                        SupportsAudioInput: inputModalities.Contains("audio") ? true : null
                    ),
                    Modality: Modality.Chat,
                    Sources: SourceLabel,
                    LastUpdated: fetchedAt
                )
            );
        }

        return new SourceSnapshot("openrouter", fetchedAt, models);
    }

    private static Pricing? ExtractPricing(JsonElement el)
    {
        if (!el.TryGetProperty("pricing", out var p))
            return null;
        var input = ParseStringDecimal(p, "prompt");
        var output = ParseStringDecimal(p, "completion");
        var cached = ParseStringDecimal(p, "input_cache_read");
        var cacheWrite = ParseStringDecimal(p, "input_cache_write");
        var reasoning = ParseStringDecimal(p, "internal_reasoning");
        if (
            input is null
            && output is null
            && cached is null
            && cacheWrite is null
            && reasoning is null
        )
            return null;
        return new Pricing(
            InputCostPerMillion: input * PerTokenToPerMillion,
            OutputCostPerMillion: output * PerTokenToPerMillion,
            CachedInputCostPerMillion: cached * PerTokenToPerMillion,
            ReasoningOutputCostPerMillion: reasoning * PerTokenToPerMillion,
            CacheWriteCostPerMillion: cacheWrite * PerTokenToPerMillion
        );
    }

    private static Context? ExtractContext(JsonElement el)
    {
        long? maxIn =
            el.TryGetProperty("context_length", out var cl) && cl.ValueKind == JsonValueKind.Number
                ? cl.GetInt64()
                : null;
        long? maxOut =
            el.TryGetProperty("top_provider", out var tp)
            && tp.TryGetProperty("max_completion_tokens", out var mct)
            && mct.ValueKind == JsonValueKind.Number
                ? mct.GetInt64()
                : null;
        return maxIn is null && maxOut is null ? null : new Context(maxIn, maxOut);
    }

    private static decimal? ParseStringDecimal(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p)
        && p.GetString() is string s
        && decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
            ? d
            : null;
}
