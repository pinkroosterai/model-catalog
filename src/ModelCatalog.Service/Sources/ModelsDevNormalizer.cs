using System.Text.Json;
using ModelCatalog.Client.Dtos;

namespace ModelCatalog.Service.Sources;

public sealed class ModelsDevNormalizer
{
    private static readonly string[] SourceLabel = ["modelsdev"];

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822",
        Justification = "Instance method for DI parity"
    )]
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Minor Code Smell",
        "S2325",
        Justification = "Instance method for DI parity"
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

        foreach (var prov in doc.RootElement.EnumerateObject())
        {
            if (prov.Value.ValueKind != JsonValueKind.Object)
                continue;
            if (!prov.Value.TryGetProperty("models", out var modelsEl))
                continue;

            var provider = prov.Name.ToLowerInvariant();
            foreach (var m in modelsEl.EnumerateObject())
            {
                var modelId =
                    m.Value.TryGetProperty("id", out var idEl) && idEl.GetString() is string s
                        ? s
                        : m.Name;

                models.Add(
                    new ModelInfo(
                        Id: $"{provider}/{modelId}".ToLowerInvariant(),
                        Provider: provider,
                        ModelId: modelId,
                        DisplayName: m.Value.TryGetProperty("name", out var n)
                            ? n.GetString()
                            : null,
                        Pricing: ExtractPricing(m.Value),
                        Context: ExtractContext(m.Value),
                        Capabilities: ExtractCapabilities(m.Value),
                        Modality: Modality.Chat,
                        Sources: SourceLabel,
                        LastUpdated: fetchedAt
                    )
                );
            }
        }

        return new SourceSnapshot("modelsdev", fetchedAt, models);
    }

    private static Pricing? ExtractPricing(JsonElement el)
    {
        if (!el.TryGetProperty("cost", out var c))
            return null;
        var input = GetNumber(c, "input");
        var output = GetNumber(c, "output");
        var cached = GetNumber(c, "cache_read");
        var cacheWrite = GetNumber(c, "cache_write");
        var reasoning = GetNumber(c, "reasoning");
        var contextOver200k = GetNumber(c, "context_over_200k");
        if (
            input is null
            && output is null
            && cached is null
            && cacheWrite is null
            && reasoning is null
            && contextOver200k is null
        )
            return null;
        return new Pricing(
            InputCostPerMillion: input,
            OutputCostPerMillion: output,
            CachedInputCostPerMillion: cached,
            ReasoningOutputCostPerMillion: reasoning,
            CacheWriteCostPerMillion: cacheWrite,
            InputCostPerMillionAboveContextThreshold: contextOver200k,
            ContextThresholdTokens: contextOver200k is null ? null : 200_000L
        );
    }

    private static decimal? GetNumber(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetDecimal()
            : null;

    private static Context? ExtractContext(JsonElement el)
    {
        if (!el.TryGetProperty("limit", out var l))
            return null;
        long? inCtx =
            l.TryGetProperty("context", out var ctx) && ctx.ValueKind == JsonValueKind.Number
                ? ctx.GetInt64()
                : null;
        long? outCtx =
            l.TryGetProperty("output", out var o) && o.ValueKind == JsonValueKind.Number
                ? o.GetInt64()
                : null;
        return inCtx is null && outCtx is null ? null : new Context(inCtx, outCtx);
    }

    private static Capabilities ExtractCapabilities(JsonElement el)
    {
        var inputModalities =
            el.TryGetProperty("modalities", out var mods)
            && mods.TryGetProperty("input", out var mi)
            && mi.ValueKind == JsonValueKind.Array
                ? mi.EnumerateArray()
                    .Select(x => x.GetString() ?? string.Empty)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new Capabilities(
            IsReasoning: GetBool(el, "reasoning"),
            SupportsFunctionCalling: GetBool(el, "tool_call"),
            SupportsResponseSchema: null,
            SupportsVision: inputModalities.Contains("image") ? true : null,
            SupportsAudioInput: inputModalities.Contains("audio") ? true : null
        );
    }

    private static bool? GetBool(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p)
        && p.ValueKind switch
        {
            JsonValueKind.True => (bool?)true,
            JsonValueKind.False => false,
            _ => null,
        }
            is bool b
            ? b
            : null;
}
