namespace ModelCatalog.Service.Merging;

public sealed class MergeOptions
{
#pragma warning disable CA1819
    public string[] PricingOrder { get; set; } = ["openrouter", "litellm", "modelsdev"];
    public string[] ContextOrder { get; set; } = ["litellm", "modelsdev", "openrouter"];
    public string[] CapabilitiesOrder { get; set; } = ["litellm", "modelsdev", "openrouter"];
    public string[] DisplayOrder { get; set; } = ["modelsdev", "litellm", "openrouter"];
#pragma warning restore CA1819
}
