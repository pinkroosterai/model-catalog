using FluentAssertions;
using Xunit;

namespace Clarive.ModelRegistry.Service.IntegrationTests;

public class AuthTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public AuthTests(TestAppFactory f)
    {
        ArgumentNullException.ThrowIfNull(f);
        _factory = f;
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var resp = await _factory.CreateClient().GetAsync(new Uri("/v1/models", UriKind.Relative));
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task WrongKey_Returns401()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", "wrong");
        var resp = await c.GetAsync(new Uri("/v1/models", UriKind.Relative));
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Healthz_IsOpen()
    {
        var resp = await _factory.CreateClient().GetAsync(new Uri("/healthz", UriKind.Relative));
        resp.StatusCode.Should().BeOneOf(System.Net.HttpStatusCode.OK, System.Net.HttpStatusCode.ServiceUnavailable);
    }
}
