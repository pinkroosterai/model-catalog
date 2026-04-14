using System.Text.Json;

namespace Clarive.ModelRegistry.Service.Aliases;

public sealed class AliasResolver
{
    private readonly IReadOnlyDictionary<string, Dictionary<string, string>> _map;

    public AliasResolver(IReadOnlyDictionary<string, Dictionary<string, string>> map) => _map = map ?? throw new ArgumentNullException(nameof(map));

    public string Resolve(string source, string sourceSpecificId)
    {
        if (_map.TryGetValue(source, out var inner)
            && inner.TryGetValue(sourceSpecificId, out var canonical))
            return canonical;
        return sourceSpecificId;
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "MA0002", Justification = "JsonSerializer.Deserialize does not accept a comparer")]
    public static AliasResolver LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return new AliasResolver(new Dictionary<string, Dictionary<string, string>>());
        var raw = File.ReadAllText(path);
        var parsed = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string>>>(raw)
            ?? new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        return new AliasResolver(parsed);
    }
}
