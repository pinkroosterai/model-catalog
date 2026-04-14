namespace Clarive.ModelRegistry.Client;

public sealed class ModelCatalogClientOptions
{
#pragma warning disable CA1056
    public string BaseUrl { get; set; } = string.Empty;
#pragma warning restore CA1056
    public string ApiKey { get; set; } = string.Empty;
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan StaleGrace { get; set; } = TimeSpan.FromHours(24);
}
