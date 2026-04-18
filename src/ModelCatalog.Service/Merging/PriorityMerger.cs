using ModelCatalog.Client.Dtos;
using ModelCatalog.Service.Aliases;
using ModelCatalog.Service.Sources;

namespace ModelCatalog.Service.Merging;

public sealed class PriorityMerger(MergeOptions options, AliasResolver aliases)
{
    public IReadOnlyList<ModelInfo> Merge(IReadOnlyList<SourceSnapshot> snapshots)
    {
        var bySource = snapshots.ToDictionary(
            s => s.SourceName,
            ResolveIds,
            StringComparer.OrdinalIgnoreCase
        );
        var allIds = bySource
            .Values.SelectMany(d => d.Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var result = new List<ModelInfo>(allIds.Count);
        foreach (var id in allIds)
        {
            var present = options
                .PricingOrder.Concat(options.ContextOrder)
                .Concat(options.CapabilitiesOrder)
                .Concat(options.DisplayOrder)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(s => bySource.TryGetValue(s, out var d) && d.ContainsKey(id))
                .ToList();

            if (present.Count == 0)
                continue;

            var pricing = MergePricing(options.PricingOrder, id, bySource);
            var context = FirstNonNull(options.ContextOrder, id, bySource, m => m.Context);
            var caps = MergeCapabilities(options.CapabilitiesOrder, id, bySource);
            var (displayName, modality) = FirstDisplay(options.DisplayOrder, id, bySource);
            var lastUpdated = present.Max(s => bySource[s][id].LastUpdated);
            var anyModel = bySource[present[0]][id];

            result.Add(
                new ModelInfo(
                    Id: id,
                    Provider: anyModel.Provider,
                    ModelId: anyModel.ModelId,
                    DisplayName: displayName,
                    Pricing: pricing,
                    Context: context,
                    Capabilities: caps,
                    Modality: modality,
                    Sources: present,
                    LastUpdated: lastUpdated
                )
            );
        }
        return result;
    }

    private Dictionary<string, ModelInfo> ResolveIds(SourceSnapshot snap)
    {
        var result = new Dictionary<string, ModelInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var m in snap.Models)
        {
            var canonical = aliases.Resolve(snap.SourceName, m.Id);
            result[canonical] = m with { Id = canonical };
        }
        return result;
    }

    private static T? FirstNonNull<T>(
        string[] order,
        string id,
        Dictionary<string, Dictionary<string, ModelInfo>> bySource,
        Func<ModelInfo, T?> pick
    )
        where T : class
    {
        foreach (var src in order)
            if (bySource.TryGetValue(src, out var d) && d.TryGetValue(id, out var m))
            {
                var v = pick(m);
                if (v is not null)
                    return v;
            }
        return null;
    }

    private static Pricing? MergePricing(
        string[] order,
        string id,
        Dictionary<string, Dictionary<string, ModelInfo>> bySource
    )
    {
        decimal? input = null,
            output = null,
            cachedIn = null,
            reasoning = null;
        decimal? cacheWrite = null,
            cacheWrite1h = null;
        decimal? tieredIn = null,
            tieredOut = null,
            batchFraction = null;
        long? threshold = null;
        string? currency = null;
        var seen = false;

        foreach (var src in order)
        {
            if (!bySource.TryGetValue(src, out var d) || !d.TryGetValue(id, out var m))
                continue;
            if (m.Pricing is not { } px)
                continue;
            seen = true;
            input ??= px.InputCostPerMillion;
            output ??= px.OutputCostPerMillion;
            cachedIn ??= px.CachedInputCostPerMillion;
            reasoning ??= px.ReasoningOutputCostPerMillion;
            cacheWrite ??= px.CacheWriteCostPerMillion;
            cacheWrite1h ??= px.CacheWrite1hCostPerMillion;
            if (tieredIn is null && tieredOut is null && px.ContextThresholdTokens is not null)
            {
                tieredIn = px.InputCostPerMillionAboveContextThreshold;
                tieredOut = px.OutputCostPerMillionAboveContextThreshold;
                threshold = px.ContextThresholdTokens;
            }
            batchFraction ??= px.BatchDiscountFraction;
            currency ??= px.Currency;
        }

        if (!seen)
            return null;
        return new Pricing(
            InputCostPerMillion: input,
            OutputCostPerMillion: output,
            CachedInputCostPerMillion: cachedIn,
            ReasoningOutputCostPerMillion: reasoning,
            CacheWriteCostPerMillion: cacheWrite,
            CacheWrite1hCostPerMillion: cacheWrite1h,
            InputCostPerMillionAboveContextThreshold: tieredIn,
            OutputCostPerMillionAboveContextThreshold: tieredOut,
            ContextThresholdTokens: threshold,
            BatchDiscountFraction: batchFraction,
            Currency: currency ?? "USD"
        );
    }

    private static Capabilities MergeCapabilities(
        string[] order,
        string id,
        Dictionary<string, Dictionary<string, ModelInfo>> bySource
    )
    {
        bool? reasoning = null,
            func = null,
            schema = null,
            vision = null,
            audio = null;
        foreach (var src in order)
        {
            if (!bySource.TryGetValue(src, out var d) || !d.TryGetValue(id, out var m))
                continue;
            reasoning ??= m.Capabilities.IsReasoning;
            func ??= m.Capabilities.SupportsFunctionCalling;
            schema ??= m.Capabilities.SupportsResponseSchema;
            vision ??= m.Capabilities.SupportsVision;
            audio ??= m.Capabilities.SupportsAudioInput;
        }
        return new Capabilities(reasoning, func, schema, vision, audio);
    }

    private static (string? DisplayName, Modality Modality) FirstDisplay(
        string[] order,
        string id,
        Dictionary<string, Dictionary<string, ModelInfo>> bySource
    )
    {
        string? name = null;
        var modality = Modality.Chat;
        foreach (var src in order)
            if (bySource.TryGetValue(src, out var d) && d.TryGetValue(id, out var m))
            {
                name ??= m.DisplayName;
                if (modality == Modality.Chat)
                    modality = m.Modality;
            }
        return (name, modality);
    }
}
