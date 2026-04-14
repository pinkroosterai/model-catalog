using System.Text.Json;

namespace ModelCatalog.Service.Catalog;

public sealed class SnapshotStore(string snapshotPath)
{
    private volatile NormalizedSnapshot? _current;

    public NormalizedSnapshot? Current => _current;

    public async Task SwapAsync(NormalizedSnapshot snapshot, CancellationToken ct)
    {
        _current = snapshot;
        Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
        var tmp = snapshotPath + ".tmp";
        var fs = File.Create(tmp);
        await using (fs.ConfigureAwait(false))
            await JsonSerializer.SerializeAsync(fs, snapshot, cancellationToken: ct).ConfigureAwait(false);
        File.Move(tmp, snapshotPath, overwrite: true);
    }

    public async Task TryLoadFromDiskAsync(CancellationToken ct)
    {
        if (!File.Exists(snapshotPath)) return;
        var fs = File.OpenRead(snapshotPath);
        await using var _ = fs.ConfigureAwait(false);
        _current = await JsonSerializer.DeserializeAsync<NormalizedSnapshot>(fs, cancellationToken: ct).ConfigureAwait(false);
    }
}
