using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ModelCatalog.Client.Dtos;

namespace ModelCatalog.Service.Sources;

public sealed class LiteLlmNormalizer
{
    private const decimal PerTokenToPerMillion = 1_000_000m;
    private static readonly string[] LiteLlmSource = ["litellm"];

    [SuppressMessage(
        "Minor Code Smell",
        "S2325:Methods and properties that don't access instance data should be static",
        Justification = "Instance API allows future DI / state without breaking callers."
    )]
    [SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "Instance API allows future DI / state without breaking callers."
    )]
    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Canonical provider/model ids are lowercase by LiteLLM convention."
    )]
    public SourceSnapshot Normalize(string rawJson, DateTimeOffset fetchedAt)
    {
        ArgumentNullException.ThrowIfNull(rawJson);

        using var doc = JsonDocument.Parse(rawJson);
        var models = new List<ModelInfo>();

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var (provider, modelId) = SplitKey(prop.Name, prop.Value);
            var canonicalId = $"{provider}/{modelId}".ToLowerInvariant();

            models.Add(
                new ModelInfo(
                    Id: canonicalId,
                    Provider: provider,
                    ModelId: modelId,
                    DisplayName: null,
                    Pricing: ExtractPricing(prop.Value),
                    Context: ExtractContext(prop.Value),
                    Capabilities: ExtractCapabilities(prop.Value),
                    Modality: MapModality(prop.Value),
                    Sources: LiteLlmSource,
                    LastUpdated: fetchedAt
                )
            );
        }

        return new SourceSnapshot("litellm", fetchedAt, models);
    }

    [SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Canonical provider ids are lowercase by LiteLLM convention."
    )]
    private static (string Provider, string ModelId) SplitKey(string key, JsonElement el)
    {
        if (el.TryGetProperty("litellm_provider", out var p) && p.GetString() is string provider)
        {
            var modelId = key.Contains('/', StringComparison.Ordinal) ? key.Split('/', 2)[1] : key;
            return (provider.ToLowerInvariant(), modelId);
        }

        if (key.Contains('/', StringComparison.Ordinal))
        {
            var parts = key.Split('/', 2);
            return (parts[0].ToLowerInvariant(), parts[1]);
        }

        return ("unknown", key);
    }

    private static readonly (string Field, long Threshold)[] TieredInputKeys =
    [
        ("input_cost_per_token_above_200k_tokens", 200_000),
        ("input_cost_per_token_above_128k_tokens", 128_000),
        ("input_cost_per_token_above_272k_tokens", 272_000),
    ];

    private static readonly (string Field, long Threshold)[] TieredOutputKeys =
    [
        ("output_cost_per_token_above_200k_tokens", 200_000),
        ("output_cost_per_token_above_128k_tokens", 128_000),
        ("output_cost_per_token_above_272k_tokens", 272_000),
    ];

    private static Pricing? ExtractPricing(JsonElement el)
    {
        var input = GetDecimal(el, "input_cost_per_token");
        var output = GetDecimal(el, "output_cost_per_token");
        var cached = GetDecimal(el, "cache_read_input_token_cost");
        var reasoning = GetDecimal(el, "output_cost_per_reasoning_token");
        var cacheWrite = GetDecimal(el, "cache_creation_input_token_cost");
        var cacheWrite1h = GetDecimal(el, "cache_creation_input_token_cost_above_1hr");

        var (tieredIn, tieredOut, threshold) = ExtractTiered(el);

        var batchInput = GetDecimal(el, "input_cost_per_token_batches");
        var batchOutput = GetDecimal(el, "output_cost_per_token_batches");
        var batchFraction = ComputeBatchFraction(input, output, batchInput, batchOutput);

        if (
            input is null
            && output is null
            && cached is null
            && reasoning is null
            && cacheWrite is null
            && cacheWrite1h is null
            && tieredIn is null
            && tieredOut is null
            && batchFraction is null
        )
        {
            return null;
        }

        return new Pricing(
            InputCostPerMillion: input * PerTokenToPerMillion,
            OutputCostPerMillion: output * PerTokenToPerMillion,
            CachedInputCostPerMillion: cached * PerTokenToPerMillion,
            ReasoningOutputCostPerMillion: reasoning * PerTokenToPerMillion,
            CacheWriteCostPerMillion: cacheWrite * PerTokenToPerMillion,
            CacheWrite1hCostPerMillion: cacheWrite1h * PerTokenToPerMillion,
            InputCostPerMillionAboveContextThreshold: tieredIn * PerTokenToPerMillion,
            OutputCostPerMillionAboveContextThreshold: tieredOut * PerTokenToPerMillion,
            ContextThresholdTokens: threshold,
            BatchDiscountFraction: batchFraction
        );
    }

    private static (decimal? Input, decimal? Output, long? Threshold) ExtractTiered(JsonElement el)
    {
        long? threshold = null;
        decimal? tieredIn = null;
        decimal? tieredOut = null;
        foreach (var (field, th) in TieredInputKeys)
        {
            var v = GetDecimal(el, field);
            if (v is null)
                continue;
            tieredIn = v;
            threshold = th;
            break;
        }
        foreach (var (field, th) in TieredOutputKeys)
        {
            var v = GetDecimal(el, field);
            if (v is null)
                continue;
            tieredOut = v;
            threshold ??= th;
            break;
        }
        return (tieredIn, tieredOut, threshold);
    }

    private static decimal? ComputeBatchFraction(
        decimal? input,
        decimal? output,
        decimal? batchInput,
        decimal? batchOutput
    )
    {
        if (input is > 0 && batchInput is not null)
            return Round(1m - batchInput.Value / input.Value);
        if (output is > 0 && batchOutput is not null)
            return Round(1m - batchOutput.Value / output.Value);
        return null;

        static decimal Round(decimal d) => Math.Round(d, 4, MidpointRounding.AwayFromZero);
    }

    private static Context? ExtractContext(JsonElement el)
    {
        var maxIn = GetLong(el, "max_input_tokens");
        var maxOut = GetLong(el, "max_output_tokens");
        return maxIn is null && maxOut is null ? null : new Context(maxIn, maxOut);
    }

    private static Capabilities ExtractCapabilities(JsonElement el) =>
        new(
            GetBool(el, "supports_reasoning"),
            GetBool(el, "supports_function_calling"),
            GetBool(el, "supports_response_schema"),
            GetBool(el, "supports_vision"),
            GetBool(el, "supports_audio_input")
        );

    private static Modality MapModality(JsonElement el)
    {
        if (el.TryGetProperty("mode", out var m) && m.GetString() is string s)
        {
            return s switch
            {
                "chat" => Modality.Chat,
                "embedding" => Modality.Embedding,
                "image_generation" => Modality.Image,
                "audio_transcription" or "audio_speech" => Modality.Audio,
                _ => Modality.Other,
            };
        }

        return Modality.Chat;
    }

    private static decimal? GetDecimal(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number
            ? p.GetDecimal()
            : null;

    private static long? GetLong(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return p.TryGetInt64(out var l) ? l : (long)p.GetDouble();
    }

    private static bool? GetBool(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p))
        {
            return null;
        }

        return p.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => (bool?)null,
        };
    }
}
