using Clarive.ModelRegistry.Client.Dtos;
using Clarive.ModelRegistry.Service.Catalog;
using Clarive.ModelRegistry.Service.Sources;
using FluentAssertions;
using Xunit;

namespace Clarive.ModelRegistry.Service.Tests.Catalog;

public class SnapshotStoreTests
{
    private static NormalizedSnapshot MakeSnap(string id, DateTimeOffset t) =>
        new(t,
            new[]
            {
                new ModelInfo(id, id.Split('/')[0], id.Split('/')[1], null, null, null,
                    new Capabilities(null, null, null, null, null), Modality.Chat, new[] { "litellm" }, t)
            },
            new SourceState[] { new("litellm", t, null) },
            Array.Empty<SourceSnapshot>());

    [Fact]
    public async Task SwapAndGet_RoundtripsViaDisk()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"snapstore-{Guid.NewGuid()}");
        try
        {
            var sut = new SnapshotStore(Path.Combine(dir, "snapshot.json"));
            var snap = MakeSnap("openai/gpt-5", DateTimeOffset.UnixEpoch);

            await sut.SwapAsync(snap, CancellationToken.None);

            sut.Current.Should().NotBeNull();
            sut.Current!.Models.Single().Id.Should().Be("openai/gpt-5");

            var reloaded = new SnapshotStore(Path.Combine(dir, "snapshot.json"));
            await reloaded.TryLoadFromDiskAsync(CancellationToken.None);
            reloaded.Current!.Models.Single().Id.Should().Be("openai/gpt-5");
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }
}
