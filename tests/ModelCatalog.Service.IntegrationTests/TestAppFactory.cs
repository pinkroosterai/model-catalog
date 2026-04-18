using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using ModelCatalog.Client.Dtos;
using ModelCatalog.Service.IntegrationTests.Fakes;
using ModelCatalog.Service.Sources;

namespace ModelCatalog.Service.IntegrationTests;

public sealed class TestAppFactory : WebApplicationFactory<Program>
{
    private readonly string _snapshotPath = Path.Combine(
        Path.GetTempPath(),
        $"snap-{Guid.NewGuid()}.json"
    );
    public List<FakeSource> Fakes { get; } = new();
    public string ApiKey { get; } = "test-key-" + Guid.NewGuid();

    protected override IHost CreateHost(IHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureHostConfiguration(cfg =>
            cfg.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ModelRegistry:SnapshotPath"] = _snapshotPath,
                    ["ModelRegistry:ApiKeys:0:Name"] = "test",
                    ["ModelRegistry:ApiKeys:0:Key"] = ApiKey,
                    ["ModelRegistry:SyncCron"] = "0 0 0 1 1 ? 2100",
                    ["ModelRegistry:RunSyncOnStartup"] = "false",
                }
            )
        );

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISource>();
            foreach (var f in Fakes)
                services.AddSingleton<ISource>(f);
        });
        return base.CreateHost(builder);
    }

    public static SourceSnapshot Snap(
        string source,
        params (string id, decimal? price, long? ctx)[] models
    )
    {
        ArgumentNullException.ThrowIfNull(models);
        return new SourceSnapshot(
            source,
            DateTimeOffset.UnixEpoch,
            models
                .Select(m => new ModelInfo(
                    m.id,
                    m.id.Split('/')[0],
                    m.id.Split('/')[1],
                    null,
                    m.price is null ? null : new Pricing(m.price, null, null, null),
                    m.ctx is null ? null : new Context(m.ctx, null),
                    new Capabilities(null, null, null, null, null),
                    Modality.Chat,
                    new[] { source },
                    DateTimeOffset.UnixEpoch
                ))
                .ToList()
        );
    }
}
