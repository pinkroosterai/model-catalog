# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

This repository is part of the estate described in
`~/ArchitectureRedesign/docs/architecture-guideline.md` — read it before adding a service or a
feature, and before changing anything operational here (compose, networks, logging, metrics,
ports).

## What this is

An aggregator service plus a NuGet client. Three upstream LLM-metadata feeds (LiteLLM,
OpenRouter, models.dev) are fetched, normalized to a common shape, merged field-by-field
by source priority, and served as a read-only snapshot. `ModelCatalog.Client` is the typed
consumer of that HTTP surface, published to nuget.org.

## Commands

```bash
dotnet build ModelCatalog.slnx                    # analyzers are warnings-as-errors in src/
dotnet test ModelCatalog.slnx                     # all 3 test projects
dotnet test tests/ModelCatalog.Service.Tests      # one project
dotnet test ModelCatalog.slnx --filter "FullyQualifiedName~PriorityMergerTests"
dotnet run --project src/ModelCatalog.Service     # syncs all 3 sources on boot, no launchSettings, so :5000

dotnet tool install --global CSharpier --version 1.2.*   # not installed by default
dotnet csharpier format src tests
dotnet csharpier check src tests                  # CI runs this before build; it fails the build
```

Always pass the `.slnx`, never a directory — CI does, and the two differ.

## Architecture

**The DTOs live in the client, not the service.** `ModelCatalog.Client/Dtos/*` (`ModelInfo`,
`Pricing`, `Capabilities`, …) are the wire contract; `ModelCatalog.Service` references the
client project to use them. Changing a DTO changes both sides at once, and because they are
positional `sealed record`s, adding a field is source-breaking for package consumers — the
README's Versioning section is the contract to honour, and the change belongs in a minor bump.

**Data flow:** `SyncPipeline.RunAsync` fans out to every registered `ISource` in parallel
(30 s per-source timeout), and a source that throws does not fail the run — it degrades to a
`SourceState` with `LastError` set, its previous `LastSuccess` preserved. The surviving
snapshots go through `AliasResolver` (per-source id → canonical id, from
`Aliases/alias-map.json`) and then `PriorityMerger`. If *every* source fails, the previous
models are kept and `FetchedAt` is not advanced.

**Merging is per-field, not per-model.** `MergeOptions` holds four independent priority
lists — `PricingOrder`, `ContextOrder`, `CapabilitiesOrder`, `DisplayOrder` — and each field
takes the first non-null value walking its own list. So one `ModelInfo` routinely carries
prices from OpenRouter and a context window from LiteLLM. Tiered long-context pricing is the
exception: the threshold and its two above-threshold rates are taken as a set from the first
source that has a threshold, so they can't be spliced from different feeds.

**Canonical ids are `provider/model-id`, lowercased.** Normalizers lowercase on the way in;
`GET /v1/models/{provider}/{modelId}` lowercases on the way out and does an ordinal match.

**Serving is in-memory.** `SnapshotStore` holds one `volatile NormalizedSnapshot`, swapped
whole; persistence is a write to `.tmp` plus an atomic `File.Move`. Read endpoints hold no
lock and `store.Current is null` means "not synced yet" → 503, not 404. On boot,
`TryLoadFromDiskAsync` restores the last snapshot before any endpoint is mapped, so a restart
serves stale data immediately rather than nothing.

**Refresh concurrency** is guarded by a static `Interlocked` flag on `SyncJob` shared with
`POST /v1/refresh` — Quartz's `[DisallowConcurrentExecution]` alone doesn't cover the manual
path. Refresh returns 202 and runs detached; a second call while one runs gets 409.

**Auth is one inline check in `RefreshEndpoints`**, not middleware. Every read endpoint is
public by design. Zero configured keys means refresh returns 503 (disabled), not 401.

**Client caching** (`ModelCatalogClient`) is TTL + stale-grace over `IDistributedCache`: a
stale entry is served *only* when the fetch fails, a 404 is cached as `null`, and a
`SemaphoreSlim` coalesces concurrent misses. Keys are namespaced `modelregistry:v1:*`.

Note the historical name: config keys, metrics, and cache keys all say `ModelRegistry` /
`model_registry_*` while the code says `ModelCatalog`. Don't "fix" this — it's the deployed
config and scraped metric surface.

## Conventions in this codebase

- `AnalysisMode=All` with `TreatWarningsAsErrors` across `src/`; tests turn both off. When an
  analyzer is genuinely wrong, suppress it *narrowly* with a `Justification` — see the
  `[SuppressMessage]` blocks in the normalizers — rather than adding to `NoWarn`.
- Logging in hot paths uses `LoggerMessage.Define` static delegates, not interpolation.
- `TimeProvider` is injected everywhere; never `DateTimeOffset.UtcNow` in service code.
- Endpoints are static `Map*Endpoints` extension methods on `RouteGroupBuilder`, wired under
  the `/v1` group in `Program.cs`.
- Adding a source means: an `ISource` + a normalizer (pure `Normalize(rawJson, fetchedAt)`,
  unit-tested against a fixture under `tests/.../Fixtures/<source>/sample.json`), an
  `AddHttpClient` registration with the shared retry+breaker policies, and its name added to
  the four `MergeOptions` lists.
- Integration tests use `TestAppFactory`, which swaps all real `ISource`s for `FakeSource`
  and parks the cron in 2100 — never let a test hit the network.

## Release

Tag `v*.*.*` on `main`. That pushes `ghcr.io/pinkroosterai/model-catalog` and packs
`ModelCatalog.Client` to nuget.org with the version from the tag — but `<Version>` in
`ModelCatalog.Client.csproj` is set by hand, so bump it in the same commit as the tag.
