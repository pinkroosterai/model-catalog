namespace ModelCatalog.Client.Dtos;

public sealed record Pricing(
    decimal? InputCostPerMillion,
    decimal? OutputCostPerMillion,
    decimal? CachedInputCostPerMillion,
    decimal? ReasoningOutputCostPerMillion,
    decimal? CacheWriteCostPerMillion = null,
    decimal? CacheWrite1hCostPerMillion = null,
    decimal? InputCostPerMillionAboveContextThreshold = null,
    decimal? OutputCostPerMillionAboveContextThreshold = null,
    long? ContextThresholdTokens = null,
    decimal? BatchDiscountFraction = null,
    string Currency = "USD"
);
