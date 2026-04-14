namespace ModelCatalog.Client.Dtos;

public sealed record Pricing(
    decimal? InputCostPerMillion,
    decimal? OutputCostPerMillion,
    decimal? CachedInputCostPerMillion,
    decimal? ReasoningOutputCostPerMillion,
    string Currency = "USD");
