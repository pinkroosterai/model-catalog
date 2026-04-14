using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ModelCatalog.Client.Dtos;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ModelCatalog.Client;

public sealed record CachedEntry<T>(T Value, DateTimeOffset StoredAt);

public sealed class ModelCatalogClient(
    HttpClient http,
    IDistributedCache cache,
    IOptions<ModelCatalogClientOptions> options,
    TimeProvider clock,
    ILogger<ModelCatalogClient> logger) : IModelCatalogClient, IDisposable
{
    public void Dispose() => _lock.Dispose();

    private static readonly Action<ILogger, string, Exception?> LogStale =
        LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1, "StaleServed"),
            "Serving stale cache for {Url}");

    private readonly ModelCatalogClientOptions _opts = options.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public Task<ModelInfo?> GetModelAsync(string canonicalId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(canonicalId);
        var parts = canonicalId.Split('/', 2);
        return GetModelAsync(parts[0], parts[1], ct);
    }

    public Task<ModelInfo?> GetModelAsync(string provider, string modelId, CancellationToken ct = default) =>
        FetchWithCacheAsync<ModelInfo?>(
            cacheKey: $"modelregistry:v1:model:{provider}/{modelId}",
            url: $"v1/models/{provider}/{modelId}",
            allow404: true, ct);

    public async Task<IReadOnlyList<ModelInfo>> ListModelsAsync(ModelQuery? query = null, CancellationToken ct = default)
    {
        var qs = query is null ? string.Empty : BuildQuery(query);
        var key = $"modelregistry:v1:list:{StableHash(qs)}";
        var res = await FetchWithCacheAsync<IReadOnlyList<ModelInfo>>(key, "v1/models" + qs, allow404: false, ct).ConfigureAwait(false);
        return res ?? [];
    }

    public async Task<CatalogMeta> GetMetaAsync(CancellationToken ct = default) =>
        (await FetchWithCacheAsync<CatalogMeta>("modelregistry:v1:meta", "v1/meta", allow404: false, ct).ConfigureAwait(false))!;

    private async Task<T?> FetchWithCacheAsync<T>(string cacheKey, string url, bool allow404, CancellationToken ct)
    {
        var fresh = await TryReadCacheAsync<T>(cacheKey, mustBeFresh: true, ct).ConfigureAwait(false);
        if (fresh is { } f) return f.Value;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            fresh = await TryReadCacheAsync<T>(cacheKey, mustBeFresh: true, ct).ConfigureAwait(false);
            if (fresh is { } f2) return f2.Value;

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_opts.RequestTimeout);
                var resp = await http.GetAsync(new Uri(url, UriKind.Relative), cts.Token).ConfigureAwait(false);
                if (allow404 && resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    await WriteCacheAsync<T>(cacheKey, default!, ct).ConfigureAwait(false);
                    return default;
                }
                resp.EnsureSuccessStatusCode();
                var dto = await resp.Content.ReadFromJsonAsync<T>(cancellationToken: ct).ConfigureAwait(false);
                await WriteCacheAsync(cacheKey, dto!, ct).ConfigureAwait(false);
                return dto;
            }
#pragma warning disable CA1031
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                var stale = await TryReadCacheAsync<T>(cacheKey, mustBeFresh: false, ct).ConfigureAwait(false);
                if (stale is { } sv)
                {
                    LogStale(logger, url, ex);
                    return sv.Value;
                }
                throw;
            }
#pragma warning restore CA1031
        }
        finally { _lock.Release(); }
    }

    private async Task<(T? Value, bool Hit)?> TryReadCacheAsync<T>(string key, bool mustBeFresh, CancellationToken ct)
    {
        var raw = await cache.GetStringAsync(key, ct).ConfigureAwait(false);
        if (raw is null) return null;
        var entry = JsonSerializer.Deserialize<CachedEntry<T>>(raw);
        if (entry is null) return null;
        var age = clock.GetUtcNow() - entry.StoredAt;
        if (mustBeFresh && age > _opts.CacheTtl) return null;
        if (!mustBeFresh && age > _opts.CacheTtl + _opts.StaleGrace) return null;
        return (entry.Value, true);
    }

    private async Task WriteCacheAsync<T>(string key, T value, CancellationToken ct)
    {
        var entry = new CachedEntry<T>(value, clock.GetUtcNow());
        var json = JsonSerializer.Serialize(entry);
        await cache.SetStringAsync(key, json,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _opts.CacheTtl + _opts.StaleGrace
            }, ct).ConfigureAwait(false);
    }

    private static string BuildQuery(ModelQuery q)
    {
        var sb = new StringBuilder("?");
        if (q.Provider is not null) sb.Append(CultureInfo.InvariantCulture, $"provider={Uri.EscapeDataString(q.Provider)}&");
        if (q.Modality is not null) sb.Append(CultureInfo.InvariantCulture, $"modality={q.Modality}&");
        if (q.IsReasoning is not null) sb.Append(CultureInfo.InvariantCulture, $"isReasoning={q.IsReasoning}&");
        if (q.SupportsFunctionCalling is not null)
            sb.Append(CultureInfo.InvariantCulture, $"supportsFunctionCalling={q.SupportsFunctionCalling}&");
        return sb.Length == 1 ? string.Empty : sb.ToString().TrimEnd('&');
    }

    private static string StableHash(string s)
    {
        unchecked
        {
            ulong h = 14695981039346656037UL;
            foreach (var c in s) { h ^= c; h *= 1099511628211UL; }
            return h.ToString("x", CultureInfo.InvariantCulture);
        }
    }
}
