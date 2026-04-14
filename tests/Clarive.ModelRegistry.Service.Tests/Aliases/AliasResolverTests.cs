using Clarive.ModelRegistry.Service.Aliases;
using FluentAssertions;
using Xunit;

namespace Clarive.ModelRegistry.Service.Tests.Aliases;

public class AliasResolverTests
{
    [Fact]
    public void Resolve_ReturnsCanonicalIdWhenMapped()
    {
        var sut = new AliasResolver(new Dictionary<string, Dictionary<string, string>>
        {
            ["openrouter"] = new() { ["openai/gpt-5-preview"] = "openai/gpt-5" }
        });

        sut.Resolve("openrouter", "openai/gpt-5-preview").Should().Be("openai/gpt-5");
    }

    [Fact]
    public void Resolve_ReturnsInputWhenUnmapped()
    {
        var sut = new AliasResolver(new Dictionary<string, Dictionary<string, string>>());
        sut.Resolve("litellm", "openai/gpt-5").Should().Be("openai/gpt-5");
    }
}
