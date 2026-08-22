# Plan: deploy the catalog on models.pinkrooster.nl

**Goal** — Run ModelCatalog as the `modelcatalog` stack behind Caddy on
`models.pinkrooster.nl`, built to the estate's operational contract, until all twelve of the
spec's success criteria pass and the stale-feed alert has fired once on purpose.
**Status** — done
**Research** — `research.md`
**Spec** — `spec.md` (status `buildable`, no blocking marks)

## Context

The DNS record for `models.pinkrooster.nl` already points at this host and the README already
advertises the endpoint, but the TLS handshake fails: Caddy has no site block, so no certificate
was ever issued. Nothing runs. The repository has a `deploy/docker-compose.yml` that publishes
`8090:8080`, names its container `model-registry`, joins no estate network, has no healthcheck,
logs plain text, and is built by a tag-triggered workflow pushing `:latest` — none of which the
guideline permits.

So this is mostly a compliance job on a service that already works. The catalog's own behaviour
— sources, alias map, `PriorityMerger` — is untouched. Four decisions were settled in the spec
and are not reopened here: estate consumers call the container over `edge`; the three feeds are
read direct rather than through Varnish, recorded as a §9 deviation; no rate limiting ships; and
`POST /v1/refresh` is 404'd at the edge.

Two findings shaped the phases. **No new alert rule is needed** — `feed-stale-daily` in
`~/ServerManagement/scripts/telemetry-alerts.sh` is a catch-all by exclusion at exactly 1.5 × a
daily cadence, so this service's feeds are covered the moment they publish the standard pair.
And **the snapshot goes on a named volume, not a bind mount**, revising a drafted line in the
spec: the image runs as uid 1654 while a Docker-created bind mount is root-owned, and
`SnapshotStore` swaps memory before it writes, so the failure would be a service that serves
correctly and persists nothing until a restart wipes it. `research.md § The bind mount would
silently fail to persist` has the evidence.

Phases split on real dependencies: the image must contain the contract changes before it is
worth pushing, it must be on ghcr before the server can pull it, and metrics must flow before an
alert can be proven.

## Phase 1 — Bring the service up to the estate's operational contract

Code only, verifiable on a laptop. Nothing here touches deployment.

**Status** — done
**Rests on**
- `~/Development/NajsPersonalAssistants/src/Assistant.Core/Observability/` still holds
  `EstateLogging.cs`, `JsonLogFormatter.cs` and `RequestId.cs` in the shape
  `research.md § Copying the estate's logging` describes.
- `SyncPipeline.RunAsync` still writes the per-source success gauge only inside its
  `if (r.State.LastSuccess is { } ls)` test — the property that keeps a never-succeeded feed
  from publishing a 1970 timestamp.
- The service's `LoggerMessage.Define` call sites still carry named `EventId`s, which is what
  the formatter reads into `event`.

**Settle first**
- How does `feed_expected_interval_seconds` stay true if `ModelRegistry:SyncCron` changes? A
  hardcoded 86400 becomes a lie the moment the schedule moves, and the alert reads it as
  contract. Answer goes under `research.md § Still open`, replacing that entry.
- Is `/healthz` renamed to `/health`? §6's checklist says `/health`; the spec keeps `/healthz`
  and marks the departure `[THIN]`. The README documents `/healthz` as public API, so a rename
  is a change to a published surface. Answer goes under `research.md § Still open`.

**Tasks**
- [x] Replace Serilog with the estate's JSON console logging, service name `modelcatalog` —
      copy the three files named in `research.md § Copying the estate's logging`; Serilog is
      wired only in `src/ModelCatalog.Service/Program.cs` and has no other job once they land.
- [x] Rename the log event identifiers to §7's dotted stable-identifier convention — the call
      sites are the `LoggerMessage.Define` fields in `src/ModelCatalog.Service/Jobs/SyncPipeline.cs`
      and `src/ModelCatalog.Client/ModelCatalogClient.cs`; §7 forbids a second spelling of an
      existing event, so pick each name once.
- [x] Give HTTP requests a `request_id` that reaches the formatter — the assistant's
      `Assistant.Web/Observability/RequestIdMiddleware.cs` is the working shape, and the
      background sync needs one too or its lines carry `-`.
- [x] Replace `model_registry_source_last_success_seconds` with the estate's pair,
      `feed_last_success_timestamp_seconds` and `feed_expected_interval_seconds`, labelled
      `feed` — metrics are declared in `src/ModelCatalog.Service/Metrics/MetricsRegistry.cs` and
      written in `SyncPipeline`; the value becomes an absolute Unix timestamp, and the
      never-succeeded property must survive as `research.md § The "no value, not zero" rule`
      describes.
- [x] Add a health probe the container healthcheck can call without curl, which the runtime
      image does not carry — the assistant's `--health` flag is the estate precedent.
- [x] Add the one-line pointer to `architecture-guideline.md` that §6 requires in `CLAUDE.md` —
      the file exists and currently has none.

**Done when** — `dotnet test ModelCatalog.slnx` passes; running the service locally writes JSON
log lines carrying `ts`, `level`, `service`, `event` and `request_id` with `service` reading
`modelcatalog`; `/metrics` shows `feed_expected_interval_seconds` for three feeds and
`feed_last_success_timestamp_seconds` only for feeds that have actually succeeded; the health
probe exits non-zero against a stopped service and zero against a running one.

## Phase 2 — Package the stack and publish the image

**Status** — done
**Rests on**
- Phase 1 merged to `main`, so the image built here contains the contract changes Phase 3
  verifies.
- `deploy/Dockerfile` still builds and still ends `USER app` with `/app/data` owned by that
  user — the named volume in Phase 3 inherits its ownership from exactly that.
- Push rights to `ghcr.io/pinkroosterai`.

**Settle first**
- ~~Are the existing `ghcr.io/pinkroosterai/model-catalog` tags deleted, left in place, or
  redirected?~~ **Answered 2026-08-22** — the package is public and the instruction to pull it
  ships inside a public NuGet package, so CI publishes both names from the same build rather than
  deleting (breaks pulls) or freezing (strands people on April code). See
  `research.md § Phase 2 settled`.

**Tasks**
- [x] Write `compose.yml` at the repository root on the estate's shape and delete
      `deploy/docker-compose.yml` — `~/Development/PromptImprover/compose.yml` is the closest
      analogue (single app container, one data directory) and carries the `x-logging` anchor,
      the two `com.pinkrooster.*` labels, `expose` rather than `ports`, and the external `edge`
      and `telemetry` networks; container `modelcatalog-api`, stack `modelcatalog`, snapshot on
      a named volume per `research.md § The bind mount would silently fail to persist`.
- [x] Add the healthcheck to that compose service, using Phase 1's probe — `start_period` must
      exceed a cold sync, which is unmeasured; the spec flags it and Phase 3 measures it.
- [x] Commit a complete `.env.example` — §5 requires every key present with no values, and
      compose must not read a key the example omits.
- [x] Move image building to the estate's shape: build on push to `main`, gated on the tests,
      pushing `<git-short-sha>` and `current` under the `modelcatalog` image name —
      `~/Development/NajsPersonalAssistants/.github/workflows/image.yml` is the pattern;
      `.github/workflows/release.yml` currently triggers on `v*.*.*` tags and pushes `:latest`.
      `.github/workflows/ci.yml` already gates `main` on `dotnet csharpier check` as well as the
      tests, so whatever shape the two workflows end up in, the formatting gate survives — the
      assistant's pattern has no equivalent and following it literally would drop it.
- [x] Keep the NuGet packaging job on its tag trigger — the package and the image release on
      genuinely different cadences and the package version is set by hand in the csproj.

**Done when** — a push to `main` produces a green workflow that pushes both
`ghcr.io/pinkroosterai/modelcatalog:<short-sha>` and `:current`; `docker compose config` in the
repository root resolves with no published ports, both external networks, and a named volume;
every variable compose interpolates appears in `.env.example`.

## Phase 3 — Deploy and wire into the estate

The first phase that touches `~/ServerManagement`. Two repositories change together.

**Status** — done
**Rests on**
- Phase 2's image is pullable from ghcr by tag.
- The external `edge` and `telemetry` networks exist on this host — both were present
  2026-08-22.
- `models.pinkrooster.nl` still resolves to `95.216.224.106`; verified 2026-08-22, and Caddy
  cannot issue a certificate if it stops.
- The `services` scrape job in `~/ServerManagement/stacks/telemetry/otel-collector-config.yaml`
  still uses the `targets` + `{service_name, service_role}` shape.

**Settle first** — Nothing. The collector-discovery question in the spec's deferred register
blocks nothing here: the scrape list is edited by hand either way.

**Tasks**
- [x] Bring the stack up from the repository root with a real `.env`, mode 600 — the volume must
      come up owned by uid 1654, so confirm the service actually persists `snapshot.json` rather
      than only serving it; `research.md` explains why that failure is silent.
- [x] Measure a cold sync — start with no snapshot and time until reads stop returning 503 —
      and set the healthcheck `start_period` above it, or Docker restarts the container mid-sync
      forever.
- [x] Add the scrape entry to the collector's `services` job and restart the collector — target
      and labels are given in `research.md § The collector entry has a fixed shape`; note the
      port is 8080, not the 8000 most of the estate uses.
- [x] Add the `models.pinkrooster.nl` site block to `~/ServerManagement/stacks/caddy/Caddyfile`
      and reload — `import accesslog` rather than `accesslog_noquery` because this service's
      query parameters are filters and carry nothing about the visitor; `/metrics` and
      `/v1/refresh` both 404 at the edge, for which the `wa` and `assistant` blocks are the
      pattern; `/healthz` stays public.
- [x] Commit the two `~/ServerManagement` edits — a change that lives only in a working tree on
      the server is invisible to the next machine.
- [x] Walk the spec's twelve success criteria and record which pass.

**Done when** — all twelve criteria in `spec.md § Success criteria` pass, including the two that
only hold if the wiring is right: `/metrics` returns 404 publicly while the collector shows the
service's metrics in OpenObserve, and `curl http://modelcatalog-api:8080/v1/meta` returns 200
from another container on `edge`.

## Phase 4 — Prove the alerting, and make the records true

**Status** — done
**Rests on**
- Phase 3 done, with all three feeds publishing `feed_last_success_timestamp_seconds` and
  reaching OpenObserve.
- `feed-stale-daily` in `~/ServerManagement/scripts/telemetry-alerts.sh` still matches by
  exclusion — `feed NOT IN (FAST_FEEDS) AND feed NOT IN (WEEKLY_FEEDS)` — so `litellm`,
  `openrouter` and `modelsdev` fall to it without being named anywhere.
- The ntfy destination the alert script provisions still works; it is shared with every other
  alert in the estate.

**Settle first**
- Should `ACME_EMAIL` be set on the Caddy stack? The spec defers it. It is estate-wide rather
  than this service's alone — without it, a renewal failure is silent for every site on this
  host — so it is raised, not decided here. Answer goes under `research.md § Still open`.

**Tasks**
- [x] Confirm the three feeds are actually matched by `feed-stale-daily` rather than assumed to
      be — the rule matches by exclusion, so a feed name colliding with `FAST_FEEDS` or
      `WEEKLY_FEEDS` would silently land in the wrong window.
- [x] Fire the stale-feed alert once on purpose and confirm the ntfy notification arrives —
      §7 is explicit that a rule which has never fired is indistinguishable from one that
      cannot, and this estate has shipped an alert that could never fire before.
- [x] Record the §4 caching deviation as a §9 row in
      `~/ArchitectureRedesign/docs/open-questions.md` — what was done, why the rule blocked it,
      and what it costs either way; the reasoning and the ruled-out alternative are in
      `spec.md § Decisions`.
- [x] Correct the README's deployment claims — it advertises `models.pinkrooster.nl` as live
      (true only after Phase 3) and names `ghcr.io/pinkroosterai/model-catalog:latest` for
      self-hosting, which Phase 2 stops publishing.

**Done when** — an ntfy notification from a deliberately-triggered stale feed has been received;
`~/ArchitectureRedesign/docs/open-questions.md` carries the §9 row; the README names the image
path CI actually pushes and describes the endpoint's real state.

## Log

**2026-08-22 — Phase 1 verify.** All three `Rests on` hold: the assistant's three Observability
files are present, `SyncPipeline` still gates the success gauge behind
`if (r.State.LastSuccess is { } ls)`, and all three `LoggerMessage.Define` sites carry named
`EventId`s.

**2026-08-22 — Phase 1 settle, and a correction that matters.** Both questions answered in
`research.md`. The second one turned on a fact the plan did not have: **this project has already
shipped.** Tags `v0.1.0` and `v0.2.0` (April 2026) ran the release workflow,
`ghcr.io/pinkroosterai/model-catalog` is a published package, and `ModelCatalog.Client` 0.1.0 and
0.2.0 are on nuget.org. The planning research asserted the service had never run, which is true
of *this host* and false of the artifacts.

Consequences: `/healthz` is a shipped surface on a pullable image, so it is kept as an alias
rather than renamed — `/health` is added beside it. And Phase 2's `Settle first` about the old
ghcr tags is now a question about a package with real consumers, not a housekeeping one. Phase 2's
entry is reworded to say so.

**2026-08-22 — Phase 1 scope note.** `feed_expected_interval_seconds` derives from the Quartz cron
expression rather than being hardcoded, so it cannot contradict `ModelRegistry:SyncCron`. Reasoning
and the irregular-schedule caveat are in `research.md`.

**2026-08-22 — Phase 1 done.** `dotnet test ModelCatalog.slnx` is 26/26 green and
`csharpier check src tests` is clean across 62 files. Verified against a running instance
rather than inferred: log lines are JSON carrying all five §7 fields with
`"service":"modelcatalog"` and a dotted `"event":"sync.completed"`; an inbound
`X-Request-Id` is echoed and correlates the request's lines; `feed_expected_interval_seconds`
reads 86400 for all three feeds, derived from the cron rather than hardcoded;
`feed_last_success_timestamp_seconds` was **absent** before the first sync and appeared
afterwards as `1787391492` = 2026-08-22 09:38:12 UTC, so §7's "no value, not zero" holds in
practice. `/health` reports the three feeds by name; `/healthz` still answers 200 unchanged.
The probe exits 1 with no snapshot, 1 against a dead port (with a distinguishable
`Connection refused` on stderr) and 0 once a snapshot exists. A real sync merged 10,273 models
from 3/3 feeds and persisted a 16 MB snapshot.

**2026-08-22 — Phase 1, an unplanned fix in the test host.** Removing Serilog broke two
integration tests with `ObjectDisposedException: LoggerFactory`. The cause is a latent defect
this change exposed rather than one it created: Quartz's `LogProvider` caches the first
`ILoggerFactory` for the lifetime of the *process*, and several test classes build and dispose
their own `TestAppFactory`, so the second host to start resolved a disposed factory. Serilog had
been masking it by binding Quartz to its own process-wide static logger. Production runs one host
per process and never hits this. The scheduler is now removed from the test host, which needs it
for nothing — `RunSyncOnStartup` is false, the cron is parked in 2100, and `/v1/refresh` drives
`SyncPipeline` directly.

**2026-08-22 — Phase 1 found: CI has been red since April, and the tests have never run.**
`.github/workflows/ci.yml` runs `dotnet csharpier check src tests`, but CSharpier 1.x installs a
`csharpier` binary and no `dotnet-csharpier` shim, so the step dies with "Could not execute
because the specified command or file was not found." Confirmed against run 32564159520 and four
earlier ones — every CI run since 2026-04-14 has failed at that step, which sits *before* build
and test, so the suite has not run in CI for four months. Left for Phase 2, which owns the
workflows; the plan's note about preserving the formatting gate now also means repairing it.


**2026-08-22 — Phase 2 built.** `compose.yml` at the root on the estate's shape, `deploy/compose`
deleted, `.env.example` complete, both workflows rewritten. `docker compose config` resolves with
no published ports, `edge` and `telemetry` both external, and the named volume
`modelcatalog-data`. Every variable compose interpolates — `IMAGE_TAG`, `EDGE_NETWORK`,
`TELEMETRY_NETWORK` — is present in `.env.example`.

**2026-08-22 — Phase 2 found: `.env` was not gitignored.** `.gitignore` covered `bin/`, `obj/`,
`data/` and editor directories but not `.env`, so the real credentials file §5 requires beside
compose would have been committed by the next `git add -A`. Added. Nothing had been committed —
checked before the fix — and the `.env` created here to validate compose carries only the empty
keys from the example.

**2026-08-22 — Phase 2: the CI repair.** `dotnet csharpier check` becomes `csharpier check` with
`$HOME/.dotnet/tools` put on `GITHUB_PATH`, which is the one-word fix for four months of red. The
image job is new and gated on `needs: build-test`, so the estate's "gated on the tests" is real
rather than nominal for the first time. `release.yml` keeps only the NuGet job: a tag-triggered
image would leave `current` pointing at the last release rather than at main.

**2026-08-22 — Phase 2 done.** CI run 32565775551 is green — the first passing run since
2026-04-14 and the first in which the test suite executed at all. Both image names published from
that build: `modelcatalog:current` and `:5d5471c`, `model-catalog:latest` and `:5d5471c`.

**2026-08-22 — Phase 2 revised after the fact.** The estate's .NET tooling note says a repository
enforcing formatting pins its own `.config/dotnet-tools.json` so CI and every working copy agree;
the global install this workflow used floats. That was already biting — this machine had CSharpier
1.3.0 against CI's `1.2.*` pin and needed a manual downgrade. The manifest pins 1.2.6 and CI runs
`dotnet tool restore` first, which also makes the original `dotnet csharpier` command correct: it
is the local-tool form, and it was the *install* that was wrong all along, not the command.

**2026-08-22 — Phase 3 done. models.pinkrooster.nl is live.** Eleven of the spec's twelve
criteria pass; the twelfth is the alert, which this plan assigns to Phase 4, so Phase 3's
`Done when` reached one criterion past its own tasks. Recorded rather than waved through.

| # | Criterion | Result |
|---|---|---|
| 1 | Model lookup with pricing, from outside this host | `openai/gpt-4o` 200, $2.50/$10.00 per M, merged from all three feeds. Also fetched from off-host: `anthropic/claude-sonnet-4.5` at $3/$15 |
| 2 | Let's Encrypt certificate | issuer `Let's Encrypt CN=YE2`, subject `models.pinkrooster.nl`, valid to 2026-11-20 |
| 3 | Healthy, no published port | `Up (healthy)`, `HostConfig.PortBindings=map[]` |
| 4 | `/metrics` 404 public, visible to the collector | 404 at the edge; the three feeds appear in OpenObserve beside the estate's existing ones |
| 5 | §7 log fields | all five present, `service=modelcatalog`, `event=sync.completed` |
| 6 | Feed metric pair | `feed_expected_interval_seconds` 86400 × 3; `feed_last_success_timestamp_seconds` × 3 |
| 7 | Stale-feed alert fired on purpose | **Phase 4** |
| 8 | Rollback by `IMAGE_TAG` | rolled to `416e5ee`, served 200, rolled forward to `current` |
| 9 | Restart serves the previous catalog | see below |
| 10 | `.env.example` complete | clean clone + example as `.env` → `docker compose config` resolves; every interpolated variable present |
| 11 | `/v1/refresh` 404 public, works inside | 404 public; 202 on `edge` with the key, 401 without |
| 12 | `/v1/meta` over `edge` | 200 from a throwaway container |

**2026-08-22 — Phase 3: criterion 9 needed a better test than the one written.** As worded — "a
snapshot age older than the process uptime" — a plain restart does not show it: `RunSyncOnStartup`
defaults to true and the sync finishes in about a second, so `FetchedAt` is newer than the process
and the measurement proves nothing either way. Re-run with `RunSyncOnStartup=false`, the property
is unambiguous: snapshot age 24s against 2.5s of uptime, 51 anthropic models served, and zero
`sync.completed` lines. The catalog came off the volume.

**2026-08-22 — Phase 3: the named volume was the right call, confirmed on the host.**
`docker exec modelcatalog-api ls -ldn /app/data` reports `1654 1654`, and a 16 MB `snapshot.json`
is written there. The bind mount the spec originally drafted would have been root-owned and
unwritable by this image's user.

**2026-08-22 — Phase 3: `start_period` set from measurement.** A cold sync took 1.17s; the worst
case is bounded near 30s by the per-source timeout. 120s was a guess made before anything ran and
is now 60s.

**2026-08-22 — Phase 4 done.** The three feeds are in `feed-stale-daily`'s scope, checked by
running the shipped rule's exact `WHERE` clause rather than by reading it. The alert was fired
deliberately — shipped SQL verbatim but for the time constant, real destination — and the
notification was read back off the ntfy topic. Temporary alert deleted and
`scripts/telemetry-alerts.sh` re-run, leaving exactly the nine alerts it defines.

Row 13 added to `~/ArchitectureRedesign/docs/open-questions.md`, recording the §4 caching
deviation as undecided with its costs. **Committed locally only — that repository has no git
remote**, unlike the guideline's claim in §5 that every repository has one. Not this work's to
fix, but the row lives on this host alone until someone gives it a remote.

The README's three false claims are corrected: the endpoint is live rather than aspirational,
the image is `modelcatalog` with `model-catalog` documented as a still-updated alias, and
`/health` is documented beside `/healthz` with the difference between them spelled out. The
absence of rate limiting and of any availability guarantee is now stated where a prospective user
reads it, rather than only in the spec.

**2026-08-22 — Phase 4 settle: `ACME_EMAIL` raised, not decided.** It stays deferred. Unset, a
renewal failure is silent for every site on this host; setting it is a change to the shared
proxy's `.env` on behalf of one service, which is the estate-wide edit §9 asks not be made
quietly. This certificate runs to 2026-11-20, so nothing is urgent.
**2026-08-22 — Close: `/code-review a5c6b8a..HEAD`, 11 findings, all worked.** Two were verified
against the running service before being accepted, and one turned out worse in context than
reported.

*Medium.* The container probe read `ASPNETCORE_HTTP_PORTS` before `ASPNETCORE_URLS`, inverting
ASP.NET Core's own precedence — and the aspnet base image sets `ASPNETCORE_HTTP_PORTS=8080`, so
an operator moving the server with `ASPNETCORE_URLS` got a container that served correctly and was
permanently unhealthy. Worse, an *empty* `ASPNETCORE_HTTP_PORTS` survived the `??` chain as `""`,
producing `http://127.0.0.1:/health`, which resolves to port 80 — where **Caddy's `:80` catch-all
answers 204 on this very host**, so the probe would have reported *healthy* while the service was
elsewhere. A false healthy is the worse direction. Both fixed and re-tested: with the server on
5311 the probe now exits 1 from the real 503 under both env shapes, with no connection error.

*Medium.* Stack traces were silently dropped from every log line — Serilog's console sink had
rendered `{Exception}`, and the copied formatter writes only type and message, which makes a feed
failure undiagnosable from the collector. A sixth field, `stack`, is added; §7 names the fields a
line must carry, not the ones it may not.

*Medium.* A first deployment whose every feed fails still writes a snapshot, because
`SyncPipeline` swaps unconditionally — empty `Models`, `FetchedAt` now. It reported healthy,
served `200 []`, passed its healthcheck, and could not be alerted on, since no feed had ever
succeeded and `feed_last_success_timestamp_seconds` is absent by design in exactly that case.
`/health` and `/healthz` now answer 503 when the catalog is empty and nothing has ever succeeded,
covered by `EmptyCatalogHealthTests`.

*Medium.* `CLAUDE.md` documented the global-install CSharpier pairing this work had just removed
from CI — the file was re-teaching the mistake that hid four months of failures.

*Low, all fixed.* The last-success metric was not re-seeded from the restored snapshot, so a
restart with `RunSyncOnStartup=false` published an interval and no last-success, and half a pair
cannot fire an alert. `model_registry_refresh_errors_total` still labelled by `source` while the
new gauges label by `feed` — two spellings of one dimension. The public `X-Request-Id` was
adopted unbounded and unvalidated. The cron default was written twice, so the scheduler and the
interval metric could drift apart — the exact thing deriving it was meant to prevent.
`release.yml`'s new header and `CLAUDE.md` both claimed the csproj version governs a release, when
`-p:Version` from the tag overrides it, and `CLAUDE.md` still described a release workflow that
no longer builds images.

**2026-08-22 — Reversal, after close: one image name.** Phase 2's `Settle first` was answered by
publishing both `modelcatalog` and `model-catalog` from one build. The user overruled that the
same day: only `ghcr.io/pinkroosterai/modelcatalog` exists. The second tag pair is out of
`ci.yml`, the name is out of the README and `CLAUDE.md`, and the reasoning that produced it is
marked reversed in `spec.md § Decisions` and `research.md` rather than deleted. The cost was
raised once and declined — a self-hoster following the April README now gets manifest-unknown.
Deleting the package itself needs a `delete:packages` token scope and is the operator's step.

