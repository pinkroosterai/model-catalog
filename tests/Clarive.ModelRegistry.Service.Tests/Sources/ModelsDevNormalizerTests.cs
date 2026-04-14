using Clarive.ModelRegistry.Client.Dtos;
using Clarive.ModelRegistry.Service.Sources;
using FluentAssertions;
using Xunit;

namespace Clarive.ModelRegistry.Service.Tests.Sources;

public class ModelsDevNormalizerTests
{
    [Fact]
    public async Task Normalize_MapsProvidersAndModels()
    {
        var json = await File.ReadAllTextAsync("Fixtures/modelsdev/sample.json");
        var sut = new ModelsDevNormalizer();

        var snapshot = sut.Normalize(json, DateTimeOffset.UnixEpoch);

        snapshot.Models.Should().HaveCount(2);
        var gpt5 = snapshot.Models.Single(m => m.Id == "openai/gpt-5");
        gpt5.DisplayName.Should().Be("GPT-5");
        gpt5.Pricing!.InputCostPerMillion.Should().Be(2.5m);
        gpt5.Context!.MaxInputTokens.Should().Be(400000);
        gpt5.Capabilities.SupportsFunctionCalling.Should().BeTrue();
        gpt5.Capabilities.SupportsVision.Should().BeTrue();
    }
}
