using FluentAssertions;
using ModelCatalog.Client.Dtos;
using ModelCatalog.Service.Sources;
using Xunit;

namespace ModelCatalog.Service.Tests.Sources;

public class LiteLlmNormalizerTests
{
    [Fact]
    public async Task Normalize_MapsChatModelFields()
    {
        var json = await File.ReadAllTextAsync("Fixtures/litellm/sample.json");
        var sut = new LiteLlmNormalizer();

        var snapshot = sut.Normalize(json, DateTimeOffset.UnixEpoch);

        snapshot.SourceName.Should().Be("litellm");
        var gpt5 = snapshot.Models.Single(m => m.Id == "openai/gpt-5");
        gpt5.Provider.Should().Be("openai");
        gpt5.ModelId.Should().Be("gpt-5");
        gpt5.Modality.Should().Be(Modality.Chat);
        gpt5.Pricing!.InputCostPerMillion.Should().Be(2.5m);
        gpt5.Pricing.OutputCostPerMillion.Should().Be(10m);
        gpt5.Context!.MaxInputTokens.Should().Be(400000);
        gpt5.Context.MaxOutputTokens.Should().Be(128000);
        gpt5.Capabilities.SupportsFunctionCalling.Should().BeTrue();
        gpt5.Capabilities.SupportsResponseSchema.Should().BeTrue();
        gpt5.Capabilities.IsReasoning.Should().BeFalse();
        gpt5.Sources.Should().Equal("litellm");
    }

    [Fact]
    public async Task Normalize_IncludesNonChatModalities()
    {
        var json = await File.ReadAllTextAsync("Fixtures/litellm/sample.json");
        var sut = new LiteLlmNormalizer();

        var snapshot = sut.Normalize(json, DateTimeOffset.UnixEpoch);

        snapshot
            .Models.Should()
            .Contain(m =>
                m.Id == "openai/text-embedding-3-small" && m.Modality == Modality.Embedding
            );
    }

    [Fact]
    public async Task Normalize_ExtractsCacheWritePricing()
    {
        var json = await File.ReadAllTextAsync("Fixtures/litellm/sample.json");
        var sut = new LiteLlmNormalizer();

        var snapshot = sut.Normalize(json, DateTimeOffset.UnixEpoch);

        var sonnet = snapshot.Models.Single(m => m.Id == "anthropic/claude-sonnet-4.6");
        sonnet.Pricing!.CachedInputCostPerMillion.Should().Be(0.3m);
        sonnet.Pricing.CacheWriteCostPerMillion.Should().Be(3.75m);
        sonnet.Pricing.CacheWrite1hCostPerMillion.Should().Be(6m);
    }

    [Fact]
    public async Task Normalize_ExtractsTieredContextPricing()
    {
        var json = await File.ReadAllTextAsync("Fixtures/litellm/sample.json");
        var sut = new LiteLlmNormalizer();

        var snapshot = sut.Normalize(json, DateTimeOffset.UnixEpoch);

        var gemini = snapshot.Models.Single(m => m.Id == "gemini/gemini-2.5-pro");
        gemini.Pricing!.InputCostPerMillion.Should().Be(1.25m);
        gemini.Pricing.InputCostPerMillionAboveContextThreshold.Should().Be(2.5m);
        gemini.Pricing.OutputCostPerMillionAboveContextThreshold.Should().Be(15m);
        gemini.Pricing.ContextThresholdTokens.Should().Be(200_000);
    }

    [Fact]
    public async Task Normalize_ComputesBatchDiscountFraction()
    {
        var json = await File.ReadAllTextAsync("Fixtures/litellm/sample.json");
        var sut = new LiteLlmNormalizer();

        var snapshot = sut.Normalize(json, DateTimeOffset.UnixEpoch);

        var gpt5 = snapshot.Models.Single(m => m.Id == "openai/gpt-5");
        gpt5.Pricing!.BatchDiscountFraction.Should().Be(0.5m);
    }
}
