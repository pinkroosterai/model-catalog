using Clarive.ModelRegistry.Client.Dtos;
using Clarive.ModelRegistry.Service.IntegrationTests.Fakes;
using Clarive.ModelRegistry.Service.Jobs;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Json;
using Xunit;

namespace Clarive.ModelRegistry.Service.IntegrationTests;

public class ModelEndpointsTests : IClassFixture<TestAppFactory>
{
    private readonly TestAppFactory _factory;

    public ModelEndpointsTests(TestAppFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
        factory.Fakes.Clear();
        factory.Fakes.Add(new FakeSource("litellm",
            _ => Task.FromResult(TestAppFactory.Snap("litellm",
                ("openai/gpt-5", 3m, 400000)))));
        factory.Fakes.Add(new FakeSource("openrouter",
            _ => Task.FromResult(TestAppFactory.Snap("openrouter",
                ("openai/gpt-5", 2.5m, null)))));
    }

    [Fact]
    public async Task GetModel_MergesAcrossSourcesByPriority()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", _factory.ApiKey);

        var pipeline = _factory.Services.GetRequiredService<SyncPipeline>();
        await pipeline.RunAsync(CancellationToken.None);

        var m = await client.GetFromJsonAsync<ModelInfo>("/v1/models/openai/gpt-5");

        m.Should().NotBeNull();
        m!.Pricing!.InputCostPerMillion.Should().Be(2.5m);
        m.Context!.MaxInputTokens.Should().Be(400000);
        m.Sources.Should().BeEquivalentTo(new[] { "litellm", "openrouter" });
    }
}
