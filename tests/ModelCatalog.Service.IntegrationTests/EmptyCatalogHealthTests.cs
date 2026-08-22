using FluentAssertions;
using ModelCatalog.Service.IntegrationTests.Fakes;
using Xunit;

namespace ModelCatalog.Service.IntegrationTests;

/// <summary>
/// A first deployment whose every feed fails still writes a snapshot — SyncPipeline swaps
/// unconditionally — so the catalog is empty while FetchedAt is now. That state must not read as
/// healthy: no feed ever succeeded, so feed_last_success_timestamp_seconds is absent by design
/// and the stale-feed alert cannot match it either.
/// </summary>
[Collection("Refresh")]
public class EmptyCatalogHealthTests
{
    [Theory]
    [InlineData("/health")]
    [InlineData("/healthz")]
    public async Task EveryFeedFailedOnAColdStart_IsNotHealthy(string path)
    {
        using var factory = new TestAppFactory();
        factory.Fakes.Add(new FakeSource("litellm", _ => throw new HttpRequestException("boom")));
        factory.Fakes.Add(
            new FakeSource("openrouter", _ => throw new HttpRequestException("boom"))
        );

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", factory.ApiKey);
        await client.PostAsync(new Uri("/v1/refresh", UriKind.Relative), content: null);
        await Task.Delay(500);

        var response = await client.GetAsync(new Uri(path, UriKind.Relative));

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
    }
}
