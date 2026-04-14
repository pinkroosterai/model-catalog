using Clarive.ModelRegistry.Service.IntegrationTests.Fakes;
using FluentAssertions;
using System.Net.Http.Json;
using Xunit;

namespace Clarive.ModelRegistry.Service.IntegrationTests;

public class RefreshEndpointTests
{
    [Fact]
    public async Task RefreshWhileAnotherIsRunning_Returns409()
    {
        using var factory = new TestAppFactory();
        factory.Fakes.Add(new FakeSource("litellm", async ct =>
        {
            await Task.Delay(2000, ct);
            return TestAppFactory.Snap("litellm", ("openai/gpt-5", 1m, 1));
        }));

        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", factory.ApiKey);

        var first = c.PostAsync(new Uri("/v1/refresh", UriKind.Relative), content: null);
        await Task.Delay(100);
        var second = await c.PostAsync(new Uri("/v1/refresh", UriKind.Relative), content: null);

        second.StatusCode.Should().Be(System.Net.HttpStatusCode.Conflict);
        (await first).StatusCode.Should().Be(System.Net.HttpStatusCode.Accepted);
    }

    [Fact]
    public async Task PartialSourceFailure_StillProducesSnapshot()
    {
        using var factory = new TestAppFactory();
        factory.Fakes.Add(new FakeSource("litellm",
            _ => Task.FromResult(TestAppFactory.Snap("litellm", ("openai/gpt-5", 3m, 400000)))));
        factory.Fakes.Add(new FakeSource("openrouter",
            _ => throw new HttpRequestException("boom")));

        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", factory.ApiKey);
        await c.PostAsync(new Uri("/v1/refresh", UriKind.Relative), content: null);
        await Task.Delay(500);

        var meta = await c.GetFromJsonAsync<Client.Dtos.CatalogMeta>(new Uri("/v1/meta", UriKind.Relative));
        meta!.SourceStates.Should().Contain(s => s.Source == "openrouter" && s.LastError != null);
        meta.SourceStates.Should().Contain(s => s.Source == "litellm" && s.LastError == null);
    }
}
