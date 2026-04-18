using FluentAssertions;
using ModelCatalog.Service.IntegrationTests.Fakes;
using Xunit;

namespace ModelCatalog.Service.IntegrationTests;

public class ColdStartTests
{
    [Fact]
    public async Task ModelsEndpoint_Returns503BeforeFirstSync()
    {
        using var factory = new TestAppFactory();
        factory.Fakes.Add(
            new FakeSource(
                "litellm",
                async ct =>
                {
                    await Task.Delay(Timeout.Infinite, ct);
                    return TestAppFactory.Snap("litellm");
                }
            )
        );

        var c = factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", factory.ApiKey);
        var resp = await c.GetAsync(new Uri("/v1/models", UriKind.Relative));
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
    }
}
