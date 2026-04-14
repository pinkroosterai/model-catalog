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
