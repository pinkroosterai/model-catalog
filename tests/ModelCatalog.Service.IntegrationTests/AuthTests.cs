using FluentAssertions;
using Xunit;

namespace ModelCatalog.Service.IntegrationTests;

public class AuthTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public AuthTests(TestAppFactory f)
    {
        ArgumentNullException.ThrowIfNull(f);
        _factory = f;
    }

    [Fact]
    public async Task Refresh_WithoutApiKey_Returns401()
    {
        var c = _factory.CreateClient();
        var resp = await c.PostAsync(new Uri("/v1/refresh", UriKind.Relative), content: null);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_WithWrongApiKey_Returns401()
    {
        var c = _factory.CreateClient();
        c.DefaultRequestHeaders.Add("X-Api-Key", "wrong-" + Guid.NewGuid());
        var resp = await c.PostAsync(new Uri("/v1/refresh", UriKind.Relative), content: null);
        resp.StatusCode.Should().Be(System.Net.HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Healthz_IsOpen()
    {
        var resp = await _factory.CreateClient().GetAsync(new Uri("/healthz", UriKind.Relative));
        resp.StatusCode.Should().BeOneOf(System.Net.HttpStatusCode.OK, System.Net.HttpStatusCode.ServiceUnavailable);
    }
}
