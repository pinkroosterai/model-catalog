using Microsoft.Extensions.Options;

namespace Clarive.ModelRegistry.Client;

public sealed class ApiKeyHandler(IOptions<ModelCatalogClientOptions> options) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.Headers.Remove("X-Api-Key");
        request.Headers.Add("X-Api-Key", options.Value.ApiKey);
        return base.SendAsync(request, cancellationToken);
    }
}
