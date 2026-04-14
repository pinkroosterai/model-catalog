using Clarive.ModelRegistry.Client.Dtos;
using Clarive.ModelRegistry.Service.Sources;
using FluentAssertions;
using Xunit;

namespace Clarive.ModelRegistry.Service.Tests.Sources;

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

        snapshot.Models.Should().Contain(m => m.Id == "openai/text-embedding-3-small"
            && m.Modality == Modality.Embedding);
    }
}
