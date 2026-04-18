using FluentAssertions;
using ModelCatalog.Client.Dtos;
using ModelCatalog.Service.Sources;
using Xunit;

namespace ModelCatalog.Service.Tests.Sources;

public class ModelsDevNormalizerTests
{
    [Fact]
    public async Task Normalize_MapsProvidersAndModels()
    {
        var json = await File.ReadAllTextAsync("Fixtures/modelsdev/sample.json");
        var sut = new ModelsDevNormalizer();

        var snapshot = sut.Normalize(json, DateTimeOffset.UnixEpoch);

        snapshot.Models.Should().HaveCount(3);
        var gpt5 = snapshot.Models.Single(m => m.Id == "openai/gpt-5");
        gpt5.DisplayName.Should().Be("GPT-5");
        gpt5.Pricing!.InputCostPerMillion.Should().Be(2.5m);
        gpt5.Context!.MaxInputTokens.Should().Be(400000);
        gpt5.Capabilities.SupportsFunctionCalling.Should().BeTrue();
        gpt5.Capabilities.SupportsVision.Should().BeTrue();
    }

    [Fact]
    public async Task Normalize_ExtractsCacheAndTieredPricing()
    {
        var json = await File.ReadAllTextAsync("Fixtures/modelsdev/sample.json");
        var sut = new ModelsDevNormalizer();

        var snapshot = sut.Normalize(json, DateTimeOffset.UnixEpoch);

        var sonnet = snapshot.Models.Single(m => m.Id == "anthropic/claude-sonnet-4.6");
        sonnet.Pricing!.CachedInputCostPerMillion.Should().Be(0.3m);
        sonnet.Pricing.CacheWriteCostPerMillion.Should().Be(3.75m);

        var gemini = snapshot.Models.Single(m => m.Id == "google/gemini-2.5-pro");
        gemini.Pricing!.InputCostPerMillionAboveContextThreshold.Should().Be(2.5m);
        gemini.Pricing.ContextThresholdTokens.Should().Be(200_000);
    }
}
