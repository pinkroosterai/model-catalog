using ModelCatalog.Client.Dtos;
using ModelCatalog.Service.Aliases;
using ModelCatalog.Service.Merging;
using ModelCatalog.Service.Sources;
using FluentAssertions;
using Xunit;

namespace ModelCatalog.Service.Tests.Merging;

public class PriorityMergerTests
{
    private static readonly DateTimeOffset T = DateTimeOffset.UnixEpoch;

    private static ModelInfo Model(string source, string id,
        decimal? input = null, long? maxIn = null, bool? funcCalling = null, string? name = null) =>
        new(id, id.Split('/')[0], id.Split('/')[1], name,
            input is null ? null : new Pricing(input, null, null, null),
            maxIn is null ? null : new Context(maxIn, null),
            new Capabilities(null, funcCalling, null, null, null),
            Modality.Chat, new[] { source }, T);

    private static AliasResolver EmptyAliases() =>
        new(new Dictionary<string, Dictionary<string, string>>());

    [Fact]
    public void Merge_PricingFollowsPriorityOrder()
    {
        var sut = new PriorityMerger(new MergeOptions(), EmptyAliases());

        var merged = sut.Merge(new[]
        {
            new SourceSnapshot("litellm", T, new[] { Model("litellm", "openai/gpt-5", input: 3m) }),
            new SourceSnapshot("openrouter", T, new[] { Model("openrouter", "openai/gpt-5", input: 2.5m) }),
        });

        merged.Single().Pricing!.InputCostPerMillion.Should().Be(2.5m);
    }

    [Fact]
    public void Merge_ContextFollowsPriorityOrder()
    {
        var sut = new PriorityMerger(new MergeOptions(), EmptyAliases());

        var merged = sut.Merge(new[]
        {
            new SourceSnapshot("openrouter", T, new[] { Model("openrouter", "openai/gpt-5", maxIn: 100) }),
            new SourceSnapshot("litellm", T, new[] { Model("litellm", "openai/gpt-5", maxIn: 400000) }),
        });

        merged.Single().Context!.MaxInputTokens.Should().Be(400000);
    }

    [Fact]
    public void Merge_UnionsSourcesList()
    {
        var sut = new PriorityMerger(new MergeOptions(), EmptyAliases());

        var merged = sut.Merge(new[]
        {
            new SourceSnapshot("litellm", T, new[] { Model("litellm", "openai/gpt-5", input: 3m) }),
            new SourceSnapshot("openrouter", T, new[] { Model("openrouter", "openai/gpt-5", input: 2.5m) }),
        });

        merged.Single().Sources.Should().BeEquivalentTo(new[] { "litellm", "openrouter" });
    }

    [Fact]
    public void Merge_DisplayNameFollowsPriorityOrder()
    {
        var sut = new PriorityMerger(new MergeOptions(), EmptyAliases());

        var merged = sut.Merge(new[]
        {
            new SourceSnapshot("litellm", T, new[] { Model("litellm", "openai/gpt-5", name: "litellm-name") }),
            new SourceSnapshot("modelsdev", T, new[] { Model("modelsdev", "openai/gpt-5", name: "GPT-5") }),
        });

        merged.Single().DisplayName.Should().Be("GPT-5");
    }
}
