using System.Text.Json;
using Clarive.ModelRegistry.Client.Dtos;

namespace Clarive.ModelRegistry.Service.Sources;

public sealed class ModelsDevNormalizer
{
    private static readonly string[] SourceLabel = ["modelsdev"];

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822", Justification = "Instance method for DI parity")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Minor Code Smell", "S2325", Justification = "Instance method for DI parity")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Globalization", "CA1308", Justification = "Canonical ids are lowercase")]
    public SourceSnapshot Normalize(string rawJson, DateTimeOffset fetchedAt)
    {
        using var doc = JsonDocument.Parse(rawJson);
        var models = new List<ModelInfo>();

        foreach (var prov in doc.RootElement.EnumerateObject())
        {
            if (prov.Value.ValueKind != JsonValueKind.Object) continue;
            if (!prov.Value.TryGetProperty("models", out var modelsEl)) continue;

            var provider = prov.Name.ToLowerInvariant();
            foreach (var m in modelsEl.EnumerateObject())
            {
                var modelId = m.Value.TryGetProperty("id", out var idEl) && idEl.GetString() is string s
                    ? s : m.Name;

                models.Add(new ModelInfo(
                    Id: $"{provider}/{modelId}".ToLowerInvariant(),
                    Provider: provider,
                    ModelId: modelId,
                    DisplayName: m.Value.TryGetProperty("name", out var n) ? n.GetString() : null,
                    Pricing: ExtractPricing(m.Value),
                    Context: ExtractContext(m.Value),
                    Capabilities: ExtractCapabilities(m.Value),
                    Modality: Modality.Chat,
                    Sources: SourceLabel,
                    LastUpdated: fetchedAt));
            }
        }

        return new SourceSnapshot("modelsdev", fetchedAt, models);
    }

    private static Pricing? ExtractPricing(JsonElement el)
    {
        if (!el.TryGetProperty("cost", out var c)) return null;
        decimal? input = c.TryGetProperty("input", out var i) && i.ValueKind == JsonValueKind.Number ? i.GetDecimal() : null;
        decimal? output = c.TryGetProperty("output", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetDecimal() : null;
        decimal? cached = c.TryGetProperty("cache_read", out var cr) && cr.ValueKind == JsonValueKind.Number ? cr.GetDecimal() : null;
        if (input is null && output is null && cached is null) return null;
        return new Pricing(input, output, cached, null);
    }

    private static Context? ExtractContext(JsonElement el)
    {
        if (!el.TryGetProperty("limit", out var l)) return null;
        long? inCtx = l.TryGetProperty("context", out var ctx) && ctx.ValueKind == JsonValueKind.Number ? ctx.GetInt64() : null;
        long? outCtx = l.TryGetProperty("output", out var o) && o.ValueKind == JsonValueKind.Number ? o.GetInt64() : null;
        return inCtx is null && outCtx is null ? null : new Context(inCtx, outCtx);
    }

    private static Capabilities ExtractCapabilities(JsonElement el)
    {
        var inputModalities = el.TryGetProperty("modalities", out var mods)
            && mods.TryGetProperty("input", out var mi)
            && mi.ValueKind == JsonValueKind.Array
            ? mi.EnumerateArray().Select(x => x.GetString() ?? string.Empty).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        return new Capabilities(
            IsReasoning: GetBool(el, "reasoning"),
            SupportsFunctionCalling: GetBool(el, "tool_call"),
            SupportsResponseSchema: null,
            SupportsVision: inputModalities.Contains("image") ? true : null,
            SupportsAudioInput: inputModalities.Contains("audio") ? true : null);
    }

    private static bool? GetBool(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind switch
        {
            JsonValueKind.True => (bool?)true,
            JsonValueKind.False => false,
            _ => null
        } is bool b ? b : null;
}
