# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

`ModelCatalog` aggregates LLM model metadata (pricing, context windows, capabilities) from three upstream sources (LiteLLM, OpenRouter, models.dev), merges them into a canonical snapshot, and serves it via an ASP.NET Core minimal-API service plus a typed NuGet client. Renamed from `Clarive.ModelRegistry`; the `ModelRegistry:*` configuration prefix is preserved for compatibility (see `appsettings.json`, `TestAppFactory`).

## Common commands

Solution file is the new slnx format: `ModelCatalog.slnx`. Target framework: `net10.0` (SDK pinned via `global.json`, `rollForward: latestMinor`).

- Restore / build: `dotnet restore ModelCatalog.slnx` · `dotnet build ModelCatalog.slnx -c Release`
- Run the service locally: `dotnet run --project src/ModelCatalog.Service` (serves `/v1/*`, `/healthz`, `/metrics`, `/openapi/v1.json`).
- Run all tests: `dotnet test ModelCatalog.slnx -c Release`
- Run a single test project: `dotnet test tests/ModelCatalog.Service.Tests` (or `…Service.IntegrationTests`, `…Client.Tests`)
- Run a single test: `dotnet test tests/ModelCatalog.Service.Tests --filter "FullyQualifiedName~PriorityMergerTests.MergesPricingByConfiguredOrder"`
- Format (required by CI): `dotnet csharpier format src tests` · Check only: `dotnet csharpier check src tests` (pinned to 1.2.* — install via `dotnet tool install --global CSharpier --version 1.2.*`).
- Docker image: `docker build -f deploy/Dockerfile -t model-catalog .` · Compose: `docker compose -f deploy/docker-compose.yml up`

## Code-quality gates

`Directory.Build.props` enforces on every project:

- `TreatWarningsAsErrors=true`, `AnalysisMode=All`, `Nullable=enable`, `ImplicitUsings=enable`.
- Analyzers: `Meziantou.Analyzer`, `SonarAnalyzer.CSharp`, `Roslynator.Analyzers` — any diagnostic breaks the build. Suppress narrowly (file-scoped `#pragma warning disable` with the specific rule IDs, matched by a `restore` at file end — see `Program.cs` for the pattern), not globally.
- `.editorconfig` disables `CA2007` (ConfigureAwait) and `S1075` (hardcoded URIs) repo-wide.
- `Directory.Packages.props` enables central package management with floating versions — add new dependencies by declaring `<PackageVersion>` there, then `<PackageReference>` (no `Version=`) in the csproj.
- Formatter: CSharpier, `printWidth=100`, 4-space indent, LF line endings (`.csharpierrc.json`). CI runs `csharpier check`; local edits must match.

## Architecture

### Service (`src/ModelCatalog.Service`)

Pipeline is `Sources → AliasResolver → PriorityMerger → SnapshotStore`, scheduled by Quartz, triggered on startup and daily at `0 0 1 * * ?` (configurable).

- **`Sources/`** — one `ISource` per upstream: `LiteLlmSource`, `OpenRouterSource`, `ModelsDevSource`. Each is paired with a `*Normalizer` that converts the upstream shape into a `SourceSnapshot` of canonical `ModelInfo`. `HttpClient`s are wired with Polly resilience (`WaitAndRetry` exponential backoff, `CircuitBreaker`) in `Program.cs`.
- **`Aliases/alias-map.json`** (copied to output) — reconciles cross-source naming via `AliasResolver`. Add mappings here to collapse duplicate IDs.
- **`Merging/PriorityMerger`** — for each canonical ID, picks fields according to per-field source-order lists in `MergeOptions` (`PricingOrder`, `ContextOrder`, `CapabilitiesOrder`, `DisplayOrder`). Capabilities merge by first-non-null per flag, not all-or-nothing. Configurable at `ModelRegistry:Merge`.
- **`Catalog/SnapshotStore`** — single in-memory `volatile NormalizedSnapshot` plus atomic swap (write-tmp-then-rename) to `ModelRegistry:SnapshotPath` (default `data/snapshot.json`). `TryLoadFromDiskAsync` runs before `app.Run()` so cold starts serve the last-known snapshot while the first sync is in flight.
- **`Jobs/SyncPipeline`** — fetches all sources in parallel with a 30 s per-source timeout. A single source failure is non-fatal: the surviving sources merge, the failed source keeps its previous `LastSuccess` in `SourceState.Error`, and metrics increment `refresh_errors_total{source=…}`. If *all* sources fail, the previous snapshot's models are retained.
- **`Jobs/SyncJob`** — Quartz job wrapping `SyncPipeline`. Exposes `TryBeginRun`/`EndRun` so both the scheduled trigger and `/v1/refresh` serialize through the same gate (no concurrent syncs).
- **`Endpoints/`** — minimal APIs mounted under `/v1`: `ModelEndpoints` (read), `SourceEndpoints`, `MetaEndpoints`, `RefreshEndpoints`. All read endpoints are public. `POST /v1/refresh` is gated inline: returns 503 if `ModelRegistry:ApiKeys` is empty, otherwise requires a matching `X-Api-Key` header.
- **`Auth/`** — `ApiKeyOptions` / `ApiKeyEntry` bind the `ModelRegistry:ApiKeys` config; authorization is applied inline in `RefreshEndpoints`. If you add a new gated endpoint, follow that same inline pattern (check `IOptionsMonitor<ApiKeyOptions>.CurrentValue.ApiKeys`, compare `X-Api-Key` with `StringComparison.Ordinal`) rather than introducing middleware — a blanket middleware would break the public-by-default read endpoints.
- **`Metrics/MetricsRegistry`** — Prometheus metrics exposed at `/metrics` via `prometheus-net`; `UseHttpMetrics()` adds per-request histograms.
- **`/healthz`** returns 503 when the snapshot is older than `ModelRegistry:StaleThresholdHours` (default 72).

### Client (`src/ModelCatalog.Client`)

Published to nuget.org as `ModelCatalog.Client` (release workflow packs on `v*.*.*` tags).

- **DTOs** in `Dtos/` (`ModelInfo`, `Pricing`, `Context`, `Capabilities`, `Modality`, `SourceState`, `CatalogMeta`) are **shared with the service** via project reference — changing a DTO changes the wire contract for every consumer. Treat as a public API.
- **`ModelCatalogClient`** layers an `IDistributedCache` over an `HttpClient`: fresh-within-TTL serves from cache, stale-within-grace is acceptable as a fallback when the upstream fails (logged via `LogStale`). A `SemaphoreSlim` coalesces concurrent misses for the same key.
- `ApiKeyHandler` is a `DelegatingHandler` that attaches `X-Api-Key` when a key is configured (only needed for `/v1/refresh`).
- `ServiceCollectionExtensions.AddModelCatalogClient` is the supported entry point; direct `new ModelCatalogClient(…)` is not the intended shape.

## Testing

Three test projects, all xUnit + FluentAssertions:

- **`ModelCatalog.Service.Tests`** — unit tests for normalizers, `AliasResolver`, `PriorityMerger`, `SnapshotStore`. Fixture data lives in `Fixtures/`.
- **`ModelCatalog.Service.IntegrationTests`** — uses `WebApplicationFactory<Program>` (see `Program.cs` ending with `public partial class Program;`). `TestAppFactory` swaps `ISource` registrations with `FakeSource` instances and supplies a unique API key and temp snapshot path per factory. Use `TestAppFactory.Snap(source, (id, price, ctx), …)` to build fake `SourceSnapshot`s.
- **`ModelCatalog.Client.Tests`** — uses `RichardSzalay.MockHttp` to drive `ModelCatalogClient` against scripted responses; exercises TTL, stale-grace, and 404 caching paths.

When adding a new source, add unit tests for the normalizer against a captured fixture under `tests/ModelCatalog.Service.Tests/Fixtures/`, register it in `Program.cs`, and extend `MergeOptions` defaults if it should participate in field priority.

## Release

- Tag `v*.*.*` on `main` → `.github/workflows/release.yml` builds and pushes `ghcr.io/<owner>/model-catalog:{tag,latest}` and packs `ModelCatalog.Client` to nuget.org (requires `NUGET_API_KEY` secret). Version is derived from the tag (`${REF_NAME#v}`), so don't hand-edit `<Version>` in `ModelCatalog.Client.csproj` for releases.

## Conventions specific to this repo

- HTTP clients always go through Polly retry + circuit-breaker — wire new `ISource` registrations the same way (`AddPolicyHandler(resiliencePolicy).AddPolicyHandler(breaker)` in `Program.cs`).
- Logging uses `LoggerMessage.Define` source-generator-style delegates (see `SyncPipeline`, `ModelCatalogClient`) — prefer this over string-interpolated `ILogger` calls to satisfy the analyzer set.
- Per-field analyzer suppressions over file/project scope; include the rule ID(s) and pair each `disable` with a `restore`.
- Configuration keys live under `ModelRegistry:*` (legacy). Do not rename the prefix without migrating `appsettings.json`, the Dockerfile `ENV`, docker-compose, and every `TestAppFactory`/doc reference in lockstep.
