using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Extensions.Http;

namespace ModelCatalog.Client;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddModelCatalogClient(this IServiceCollection services,
        Action<ModelCatalogClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.Configure(configure);
        services.TryAddSingleton<IDistributedCache>(_ =>
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        services.TryAddSingleton(TimeProvider.System);
        services.AddTransient<ApiKeyHandler>();

        services.AddHttpClient<IModelCatalogClient, ModelCatalogClient>((sp, c) =>
        {
            var opts = sp.GetRequiredService<IOptions<ModelCatalogClientOptions>>().Value;
            c.BaseAddress = new Uri(opts.BaseUrl);
            c.Timeout = opts.RequestTimeout;
        })
        .AddHttpMessageHandler<ApiKeyHandler>()
        .AddPolicyHandler(HttpPolicyExtensions.HandleTransientHttpError()
            .WaitAndRetryAsync(3, a => TimeSpan.FromSeconds(Math.Pow(4, a - 1))))
        .AddPolicyHandler(HttpPolicyExtensions.HandleTransientHttpError()
            .CircuitBreakerAsync(5, TimeSpan.FromMinutes(5)));

        return services;
    }
}
