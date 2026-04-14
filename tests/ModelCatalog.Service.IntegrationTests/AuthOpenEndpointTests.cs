using ModelCatalog.Service.IntegrationTests.Fakes;
using FluentAssertions;
using Xunit;

namespace ModelCatalog.Service.IntegrationTests;

[Collection("Refresh")]
public class AuthOpenEndpointTests
{
    [Fact]
    public async Task ModelsMetaSources_AreOpen_WithoutApiKey()
    {
        using var factory = new TestAppFactory();
        factory.Fakes.Add(new FakeSource("litellm",
            _ => Task.FromResult(TestAppFactory.Snap("litellm", ("openai/gpt-5", 1m, 1)))));

        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", factory.ApiKey);
        await c.PostAsync(new Uri("/v1/refresh", UriKind.Relative), content: null);
        await Task.Delay(500);
        c.DefaultRequestHeaders.Remove("X-Api-Key");

        var models = await c.GetAsync(new Uri("/v1/models", UriKind.Relative));
        models.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var meta = await c.GetAsync(new Uri("/v1/meta", UriKind.Relative));
        meta.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);

        var sources = await c.GetAsync(new Uri("/v1/sources", UriKind.Relative));
        sources.StatusCode.Should().Be(System.Net.HttpStatusCode.OK);
    }
}
