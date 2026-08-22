using FluentAssertions;
using Xunit;

namespace ModelCatalog.Service.IntegrationTests;

/// <summary>
/// A .env scaffolded from .env.example sets every key to the empty string. That must leave
/// refresh disabled, not open: an entry that exists but is blank makes the configured count
/// non-zero while matching a request that sends an empty X-Api-Key header.
/// </summary>
[Collection("Refresh")]
public class RefreshKeyConfigTests
{
    [Fact]
    public async Task BlankConfiguredKey_LeavesRefreshDisabled()
    {
        using var factory = new TestAppFactory { ConfiguredApiKey = "" };
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "");

        var response = await client.PostAsync(
            new Uri("/v1/refresh", UriKind.Relative),
            content: null
        );

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task EmptyHeader_DoesNotAuthorise_WhenARealKeyIsConfigured()
    {
        using var factory = new TestAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "");

        var response = await client.PostAsync(
            new Uri("/v1/refresh", UriKind.Relative),
            content: null
        );

        response.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }
}
