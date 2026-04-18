using FluentAssertions;
using ModelCatalog.Client.Dtos;
using ModelCatalog.Service.Sources;
using Xunit;

namespace ModelCatalog.Service.Tests.Sources;

public class OpenRouterNormalizerTests
{
    [Fact]
    public async Task Normalize_MapsPricingContextAndCapabilities()
    {
        var json = await File.ReadAllTextAsync("Fixtures/openrouter/sample.json");
        var sut = new OpenRouterNormalizer();

        var snapshot = sut.Normalize(json, DateTimeOffset.UnixEpoch);

        var gpt5 = snapshot.Models.Single(m => m.Id == "openai/gpt-5");
        gpt5.Pricing!.InputCostPerMillion.Should().Be(2.5m);
        gpt5.Pricing.OutputCostPerMillion.Should().Be(10m);
        gpt5.Context!.MaxInputTokens.Should().Be(400000);
        gpt5.Context.MaxOutputTokens.Should().Be(128000);
        gpt5.Capabilities.SupportsFunctionCalling.Should().BeTrue();
        gpt5.Capabilities.SupportsResponseSchema.Should().BeTrue();
        gpt5.Capabilities.SupportsVision.Should().BeTrue();
        gpt5.DisplayName.Should().Be("GPT-5");

        var sonnet = snapshot.Models.Single(m => m.Id == "anthropic/claude-sonnet-4.6");
        sonnet.Capabilities.IsReasoning.Should().BeTrue();
        sonnet.Pricing!.CachedInputCostPerMillion.Should().Be(0.3m);
        sonnet.Pricing.CacheWriteCostPerMillion.Should().Be(3.75m);
    }
}
