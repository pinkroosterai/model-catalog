# ModelCatalog

A canonical catalog of LLM model metadata — pricing, context windows, capabilities — aggregated from [LiteLLM](https://github.com/BerriAI/litellm), [OpenRouter](https://openrouter.ai), and [models.dev](https://models.dev). Self-host or use the public PinkRoosterAI instance.

## Public endpoint

```
https://models.pinkrooster.nl
```

Read-only, no authentication. Intended for low-volume use; if you need higher throughput or stronger SLAs, self-host (see below).

## Quickstart

### .NET client

```bash
dotnet add package ModelCatalog.Client
```

```csharp
using ModelCatalog.Client;

services.AddModelCatalogClient(opts =>
{
    opts.BaseUrl = "https://models.pinkrooster.nl";
});
```

```csharp
public sealed class MyService(IModelCatalogClient catalog)
{
    public async Task<decimal?> GetPriceAsync(string provider, string modelId, CancellationToken ct)
    {
        var info = await catalog.GetModelAsync(provider, modelId, ct);
        return info?.Pricing?.InputCostPerMillion;
    }
}
```

### Raw HTTP

```bash
curl https://models.pinkrooster.nl/v1/models/openai/gpt-4o
```

## Client SDK

The `ModelCatalog.Client` NuGet package ships a typed client with request coalescing, response caching, transient-error retries, a circuit breaker, and stale-grace fallback when the service is unreachable. Targets `net10.0`.

### Registration

```csharp
using ModelCatalog.Client;

builder.Services.AddModelCatalogClient(opts =>
{
    opts.BaseUrl = "https://models.pinkrooster.nl";
    // opts.ApiKey = "..."; // only needed to call POST /v1/refresh
});
```

`AddModelCatalogClient` wires up:

- `IModelCatalogClient` bound to a typed `HttpClient`
- Polly retry (3 attempts, exponential backoff 1s/4s/16s) + circuit breaker (5 failures → 5 min open)
- An `ApiKeyHandler` `DelegatingHandler` that attaches `X-Api-Key` on every outbound request
- An in-memory `IDistributedCache` by default — register `AddStackExchangeRedisCache(...)` (or any other `IDistributedCache` implementation) **before** the call to share cache across processes
- `TimeProvider.System`

All registrations use `TryAdd*`, so your own `IDistributedCache`, `TimeProvider`, or `ILoggerFactory` wins.

### Surface

```csharp
public interface IModelCatalogClient
{
    Task<ModelInfo?> GetModelAsync(string canonicalId, CancellationToken ct = default);
    Task<ModelInfo?> GetModelAsync(string provider, string modelId, CancellationToken ct = default);
    Task<IReadOnlyList<ModelInfo>> ListModelsAsync(ModelQuery? query = null, CancellationToken ct = default);
    Task<CatalogMeta>              GetMetaAsync(CancellationToken ct = default);
}
```

- `GetModelAsync` returns `null` when the model is not known to the catalog. `null` is cached, so repeated unknown-model lookups don't hammer the service.
- `ListModelsAsync` accepts a `ModelQuery` (`Provider`, `Modality`, `IsReasoning`, `SupportsFunctionCalling`). Each distinct query hits a distinct cache key.
- `GetMetaAsync` reports the service-side snapshot timestamp, per-source state, and a `Healthy` flag derived from the service's stale-threshold.

### Options

| Option           | Default  | Purpose |
|------------------|----------|---------|
| `BaseUrl`        | required | ModelCatalog service URL. |
| `ApiKey`         | empty    | Attached as `X-Api-Key` on every request. Only `POST /v1/refresh` validates it; read endpoints ignore it. |
| `CacheTtl`       | 1 h      | Fresh window. Within this, responses are served from the distributed cache with no network. |
| `StaleGrace`     | 24 h     | Grace window past `CacheTtl` during which a stale cached value is served as a fallback **only** if the upstream fetch fails. A successful fetch always overwrites the cache. |
| `RequestTimeout` | 10 s     | Per-request `HttpClient.Timeout`, also imposed via a linked `CancellationTokenSource`. |

### Caching semantics

1. **Fresh hit** (`age ≤ CacheTtl`) — returned immediately, no network.
2. **Fresh miss** — a `SemaphoreSlim` coalesces concurrent callers for the same key so only one fetches; the rest read the resulting cache entry.
3. **Fetch failure** — if a stale entry exists within `CacheTtl + StaleGrace`, it's returned and a `StaleServed` warning is logged. Otherwise the original exception propagates.
4. **404 on `GetModelAsync`** — persisted as a `null` entry so repeated unknown-model lookups stay cheap.

Cache keys are namespaced `modelregistry:v1:*`, safe to share a Redis instance with other services.

### Calculating the cost of a call

The main reason to use this catalog is precise cost accounting. The `Pricing` record carries every rate a real API call can be charged against:

```csharp
public sealed record Pricing(
    decimal? InputCostPerMillion,
    decimal? OutputCostPerMillion,
    decimal? CachedInputCostPerMillion,
    decimal? ReasoningOutputCostPerMillion,
    decimal? CacheWriteCostPerMillion                    = null, // 5-min TTL (Anthropic) / first-token write (OpenAI, Gemini)
    decimal? CacheWrite1hCostPerMillion                  = null, // Anthropic 1-hour TTL
    decimal? InputCostPerMillionAboveContextThreshold    = null,
    decimal? OutputCostPerMillionAboveContextThreshold   = null,
    long?    ContextThresholdTokens                      = null, // e.g. 200_000 for Gemini 2.5 Pro
    decimal? BatchDiscountFraction                       = null, // e.g. 0.5 for async batch
    string   Currency                                    = "USD");
```

A reference estimator that handles tiered long-context pricing, prompt-caching (with Anthropic's 5-min / 1-hour TTL split), reasoning tokens, and the async Batch API discount:

```csharp
public sealed record Usage(
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens = 0,
    long CacheWriteTokens  = 0,
    long ReasoningTokens   = 0);

public static decimal? EstimateCost(
    Pricing? p, Usage u, bool batch = false, bool oneHourCacheTtl = false)
{
    if (p is null) return null;

    static decimal Per(long tokens, decimal? ratePerMillion) =>
        ratePerMillion is null ? 0m : tokens * ratePerMillion.Value / 1_000_000m;

    // Split input tokens across the long-context threshold, if the model has one.
    decimal inputCost;
    decimal? outputRate = p.OutputCostPerMillion;
    if (p.ContextThresholdTokens is { } t && u.InputTokens > t)
    {
        inputCost = Per(t, p.InputCostPerMillion)
                  + Per(u.InputTokens - t, p.InputCostPerMillionAboveContextThreshold
                                              ?? p.InputCostPerMillion);
        outputRate = p.OutputCostPerMillionAboveContextThreshold ?? outputRate;
    }
    else
    {
        inputCost = Per(u.InputTokens, p.InputCostPerMillion);
    }

    var writeRate = oneHourCacheTtl
        ? p.CacheWrite1hCostPerMillion ?? p.CacheWriteCostPerMillion
        : p.CacheWriteCostPerMillion;

    var total =
          inputCost
        + Per(u.CachedInputTokens, p.CachedInputCostPerMillion)
        + Per(u.CacheWriteTokens,  writeRate)
        + Per(u.OutputTokens,      outputRate)
        + Per(u.ReasoningTokens,   p.ReasoningOutputCostPerMillion ?? outputRate);

    if (batch && p.BatchDiscountFraction is { } fraction)
        total *= 1m - fraction;

    return total;
}
```

Usage:

```csharp
var model = await catalog.GetModelAsync("anthropic", "claude-sonnet-4.6", ct);

var cost = EstimateCost(model?.Pricing, new Usage(
    InputTokens:       12_000,
    OutputTokens:      800,
    CacheWriteTokens:  4_000));
// → cost is in model.Pricing.Currency (USD by default)
```

All pricing fields are nullable — `null` means "the upstream sources don't know this rate," not "free." Fall back to headline `InputCostPerMillion` / `OutputCostPerMillion` when a specialized rate is missing (the reference estimator above does this automatically).

### DTO reference

| Type           | Key fields |
|----------------|-----------|
| `ModelInfo`    | `Id` (`provider/model-id`), `Provider`, `ModelId`, `DisplayName?`, `Pricing?`, `Context?`, `Capabilities`, `Modality`, `Sources` (which upstream feeds contributed), `LastUpdated` |
| `Pricing`      | see above |
| `Context`      | `MaxInputTokens?`, `MaxOutputTokens?` |
| `Capabilities` | `IsReasoning?`, `SupportsFunctionCalling?`, `SupportsResponseSchema?`, `SupportsVision?`, `SupportsAudioInput?` — tri-state; `null` means unknown, not unsupported |
| `Modality`     | `Chat`, `Embedding`, `Image`, `Audio`, `Other` |
| `ModelQuery`   | `Provider?`, `Modality?`, `IsReasoning?`, `SupportsFunctionCalling?` |
| `CatalogMeta`  | `SnapshotAt`, `Staleness`, `SourceStates[]`, `Healthy` |
| `SourceState`  | `Source`, `LastSuccess?`, `LastError?` |

All DTOs are `sealed record` types, safe to use with `with { … }` patterns and positional deconstruction.

### Versioning

The DTOs are shared with the service and form the wire contract. While the package is pre-1.0, **minor-version bumps (0.x.0) are source-breaking** — new nullable fields slot into the record's positional constructor, which changes the deconstruct and `Equals` shape even though the field-access surface stays compatible.

- `0.1.x` → `0.2.0`: added cache-write, 1-hour-TTL cache-write, tiered long-context, and batch-discount fields to `Pricing`.

## API reference

| Endpoint | Auth | Description |
|---|---|---|
| `GET /healthz` | open | Liveness; returns staleness of last sync |
| `GET /metrics` | open | Prometheus metrics |
| `GET /v1/models` | open | All models, optionally filtered by `?provider=` `?modality=` |
| `GET /v1/models/{provider}/{modelId}` | open | Single model |
| `GET /v1/meta` | open | Last fetch time + per-source health |
| `GET /v1/sources` | open | Per-source state |
| `POST /v1/refresh` | api-key | Operator-triggered manual sync |

## Self-hosting

```bash
docker run -d \
  --name model-catalog \
  -p 8080:8080 \
  -v model-catalog-data:/app/data \
  ghcr.io/pinkroosterai/model-catalog:latest
```

To enable `/v1/refresh`, set at least one API key:

```bash
docker run -d \
  ... \
  -e ModelRegistry__ApiKeys__0__Name=admin \
  -e ModelRegistry__ApiKeys__0__Key="$(openssl rand -hex 32)" \
  ghcr.io/pinkroosterai/model-catalog:latest
```

The service fetches all three upstream catalogs on startup, then daily at 01:00 UTC.

## How it works

1. **Sources** — Three pluggable normalizers (`LiteLlmSource`, `OpenRouterSource`, `ModelsDevSource`) fetch + canonicalize per-source data.
2. **Aliases** — A static map (`Aliases/alias-map.json`) reconciles cross-source naming differences (e.g. `gpt-4o-2024-05-13` ↔ `gpt-4o`).
3. **Merging** — A per-field `PriorityMerger` picks the highest-trust value (default order: OpenRouter > LiteLLM > models.dev for prices; LiteLLM > others for context windows). Configurable via `ModelRegistry:Merge` in appsettings.
4. **Snapshot** — Merged catalog persisted to `data/snapshot.json`. Read endpoints serve this snapshot in-memory.
5. **Refresh** — Quartz cron + manual `/v1/refresh`. Fetch failures from one source don't break the snapshot — the others succeed and the failed source surfaces in `/v1/meta`.

## Configuration

All configuration lives under the `ModelRegistry` section. Defaults are sensible; override via `appsettings.json` or `ModelRegistry__*` environment variables.

| Key | Default | Purpose |
|---|---|---|
| `ModelRegistry:SnapshotPath` | `data/snapshot.json` | Where the merged catalog persists |
| `ModelRegistry:StaleThresholdHours` | `72` | Healthz returns 503 if older than this |
| `ModelRegistry:SyncCron` | `0 0 1 * * ?` | Daily sync schedule (Quartz cron) |
| `ModelRegistry:RunSyncOnStartup` | `true` | Trigger sync immediately on boot |
| `ModelRegistry:Sources:LiteLlm:Url` | LiteLLM GitHub raw URL | Override for testing/forks |
| `ModelRegistry:Sources:OpenRouter:Url` | `https://openrouter.ai/api/v1/` | |
| `ModelRegistry:Sources:ModelsDev:Url` | `https://models.dev/` | |
| `ModelRegistry:ApiKeys:N:Name` / `Key` | (empty) | Required for `/v1/refresh`; if empty, refresh returns 503 |

## License

[MIT](LICENSE) © 2026 PinkRoosterAI
