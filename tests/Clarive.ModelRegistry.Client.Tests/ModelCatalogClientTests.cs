using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Clarive.ModelRegistry.Client;
using Clarive.ModelRegistry.Client.Dtos;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RichardSzalay.MockHttp;
using Xunit;

namespace Clarive.ModelRegistry.Client.Tests;

public class ModelCatalogClientTests
{
    private static (ModelCatalogClient client, MockHttpMessageHandler handler, IDistributedCache cache) Build(
        ModelCatalogClientOptions? opts = null)
    {
        opts ??= new ModelCatalogClientOptions { BaseUrl = "http://fake/", ApiKey = "k" };
        var handler = new MockHttpMessageHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri(opts.BaseUrl) };
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var client = new ModelCatalogClient(http, cache, Options.Create(opts), TimeProvider.System,
            NullLogger<ModelCatalogClient>.Instance);
        return (client, handler, cache);
    }

    private static ModelInfo Sample() =>
        new("openai/gpt-5", "openai", "gpt-5", null, null, null,
            new Capabilities(null, null, null, null, null), Modality.Chat, new[] { "litellm" }, DateTimeOffset.UnixEpoch);

    [Fact]
    public async Task GetModel_HitsNetworkThenCache()
    {
        var (sut, handler, _) = Build();
        var req = handler.When("http://fake/v1/models/openai/gpt-5").Respond(JsonContent.Create(Sample()));

        var first = await sut.GetModelAsync("openai", "gpt-5");
        var second = await sut.GetModelAsync("openai", "gpt-5");

        first!.Id.Should().Be("openai/gpt-5");
        second!.Id.Should().Be("openai/gpt-5");
        handler.GetMatchCount(req).Should().Be(1);
    }

    [Fact]
    public async Task GetModel_ReturnsStaleCache_WhenServiceUnreachable()
    {
        var opts = new ModelCatalogClientOptions
        {
            BaseUrl = "http://fake/",
            ApiKey = "k",
            CacheTtl = TimeSpan.FromMilliseconds(10),
            StaleGrace = TimeSpan.FromHours(24),
        };
        var (sut, handler, cache) = Build(opts);

        var stale = new CachedEntry<ModelInfo?>(Sample(), DateTimeOffset.UtcNow.AddMinutes(-1));
        await cache.SetStringAsync("modelregistry:v1:model:openai/gpt-5",
            JsonSerializer.Serialize(stale),
            new DistributedCacheEntryOptions());
        handler.When("http://fake/v1/models/openai/gpt-5").Respond(HttpStatusCode.ServiceUnavailable);

        var result = await sut.GetModelAsync("openai", "gpt-5");
        result!.Id.Should().Be("openai/gpt-5");
    }
}
