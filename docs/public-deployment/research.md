# Research: deploying model-catalog on models.pinkrooster.nl

Checked 2026-08-22. Feeds `spec.md`.

Most of this is estate ground truth read off the running host rather than the web. The
architecture guideline governs, and where the host disagrees with the guideline that is
recorded here rather than smoothed over.

## Is it already deployed?

No. `models.pinkrooster.nl` resolves to `95.216.224.106`, which is this host, but the TLS
handshake fails with `tlsv1 alert internal error` — Caddy has no site block for that hostname,
so it has never provisioned a certificate. No `modelcatalog-*` or `model-registry` container is
running.

The README already advertises `https://models.pinkrooster.nl` as a live public endpoint. That
claim is currently false, and the DNS record pointing at a host that answers nothing is the
half of the work already done.

Source: `dig +short models.pinkrooster.nl`, `curl https://models.pinkrooster.nl/healthz`,
`docker ps`, all on this host, 2026-08-22.

## How a service becomes reachable here

One site block per service in `~/ServerManagement/stacks/caddy/Caddyfile`, reverse-proxying to
the container's name on the shared external `edge` network. TLS is automatic once the block
exists and DNS points here. Reload without downtime:

```
docker compose exec caddy-proxy caddy reload --config /etc/caddy/Caddyfile
```

Every block imports `accesslog` (JSON to stdout) or `accesslog_noquery` (query string stripped
before writing). `accesslog_noquery` exists for sites whose query parameters carry visitor
data; this service's query parameters are `?provider=`/`?modality=` filters, which are not.

Three existing blocks — `wa`, `assistant`, `mail` — deliberately 404 their own `/health` and
`/metrics` at the edge, because the collector reaches `/metrics` over the internal `telemetry`
network and publishing internal counters through the public door would be odd. That is the
established pattern for a service whose metrics should not be public.

Source: `~/ServerManagement/stacks/caddy/Caddyfile`, read 2026-08-22.

## The compose shape every stack here follows

Read off `~/Development/PromptImprover/compose.yml` and
`~/Development/NajsPersonalAssistants/compose.yml`, which agree:

- `compose.yml` at the repository root, with `name: <stack>` at the top.
- An `x-logging` YAML anchor applied to every container: `json-file` driver,
  `labels: com.pinkrooster.stack,com.pinkrooster.role`, `max-size: 10m`, `max-file: "3"`. The
  two labels are what let the collector identify a line; without them a line carries only a
  container id. Rotation is there because json-file grows without limit.
- `com.pinkrooster.stack` / `com.pinkrooster.role` also set as container `labels:`.
- `image: ghcr.io/pinkroosterai/<name>:${IMAGE_TAG:-current}` — rollback is setting
  `IMAGE_TAG` to a short sha in `.env`.
- `expose:`, never `ports:`.
- Volumes and networks named explicitly; `networks: edge` and `telemetry` both
  `external: true` with `name:` from an overridable variable.
- Data under `${DATA_ROOT:-/srv}/<stack>/...` on the PromptImprover pattern, or a named
  volume on the assistant pattern.

Source: both files, read 2026-08-22.

## Metrics are not auto-discovered — the collector names every target

The guideline says "a new service joins `telemetry` itself; the telemetry stack never names
the services it collects from." **The running config contradicts this.**
`~/ServerManagement/stacks/telemetry/otel-collector-config.yaml` has a static
`job_name: services` scrape job listing every target by container name and port —
`promptimprover-app:8000`, `ember-app:8000`, `assistant-web:8000`, `assistant-worker:9464`,
and so on. A comment on the assistant entry states it outright: "a service is invisible until
it is."

So deployment requires an edit to a second repository, `~/ServerManagement`, and joining
`telemetry` alone is not enough. This is a discrepancy in the guideline, not a choice for this
spec; it is carried into the spec's deferred register so it can be raised under §9.

Source: `~/ServerManagement/stacks/telemetry/otel-collector-config.yaml`, read 2026-08-22.

## The estate has already solved §7 JSON logging in .NET

`NajsPersonalAssistants` (the `assistant` stack) is .NET 10 and implements the §7 field
contract directly against `ILogger` in `src/Assistant.Core/Observability/`:
`JsonLogFormatter.cs` writes `ts`, `level`, `service`, `event`, `request_id`; `RequestId.cs`
and `Assistant.Web/Observability/RequestIdMiddleware.cs` carry the correlation id. It does not
use Serilog.

`model-catalog` currently configures Serilog with a plain `WriteTo.Console()`, which emits
human-readable text and none of the five contract fields. Either the formatter is copied from
the assistant or Serilog is configured to emit the same shape.

The guideline is explicit that this setup is copied per service rather than imported from a
shared package, and that `najs-logging` gets extracted "the second time the contract changes
and three copies have to be edited together." Copying is still the sanctioned move.

Source: `~/Development/NajsPersonalAssistants/src/Assistant.Core/Observability/`,
`src/ModelCatalog.Service/Program.cs`, both read 2026-08-22.

## Healthchecks without curl

The .NET runtime images carry no curl. The assistant's answer is a flag on the app itself:

```yaml
test: ["CMD", "dotnet", "Assistant.Web.dll", "--health"]
```

`model-catalog`'s runtime base is `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`, whose busybox
does provide `wget`, so `wget -q -O- http://127.0.0.1:8080/healthz` would also work. The
assistant's flag is the more portable of the two and does not depend on the base image keeping
busybox.

Source: `~/Development/NajsPersonalAssistants/compose.yml`, `deploy/Dockerfile`, read
2026-08-22.

## How images are built and tagged here

The assistant's `.github/workflows/image.yml` builds on push to `main` (plus
`workflow_dispatch`), runs `dotnet test --warnaserror` as a gate, then pushes two tags:
`ghcr.io/pinkroosterai/<name>:<git-short-sha>` and `:current`. Concurrency group cancels
superseded builds because Actions minutes on private repos are a monthly quota.

`model-catalog`'s `release.yml` differs on every point: it triggers only on `v*.*.*` tags and
pushes `:<tag>` and `:latest`. `:latest` is not a tag anything in this estate pulls, and there
is no short-sha tag, so the estate's one-line rollback does not exist for this service.

Source: `~/Development/NajsPersonalAssistants/.github/workflows/image.yml`,
`.github/workflows/release.yml`, read 2026-08-22.

## Third-party reads and the outbound cache

`~/ServerManagement/stacks/varnish/vcl/default.vcl` is the route table, with dynamic backends
for the bike and geo feeds — Donkey, OV-fiets, Dott, PDOK. Nothing LLM-related. The guideline's
rule is "third parties are cached when the answer is stable and read directly when caching
would make it wrong," with slow-moving metadata going through Varnish.

This service reads three third-party documents once a day. Caching a document fetched once
daily saves nothing on transfer and adds a hop, so the rule's *purpose* — collapsing repeated
reads and holding one set of upstream credentials — does not bite here. But the rule is written
as a rule, and none of these three feeds needs a credential. Whether the sync goes direct or
through Varnish is put to the user rather than assumed, because deviating is a §9 act.

Source: `~/ServerManagement/stacks/varnish/vcl/default.vcl`, read 2026-08-22.

## Rate limiting is not in Caddy's core

Caddy 2 has no built-in HTTP rate limiter. The usual module is `mholt/caddy-ratelimit`, written
by a core Caddy developer but explicitly "not an official repository of the Caddy Web Server
organization." Using it means building Caddy with `xcaddy` instead of pulling the pinned
`caddy:2.11.4-alpine` image the estate runs today.

That is an estate-wide change to the one stack every service sits behind, to protect one
service — which makes it a decision worth putting rather than taking.

Source: [caddy-ratelimit README](https://github.com/mholt/caddy-ratelimit), checked 2026-08-22;
running image from `docker ps` on this host, 2026-08-22.

## Where the repository stands against the §6 checklist

Read off the repository at commit `a5c6b8a`, 2026-08-22.

| Checklist item | State |
|---|---|
| Compose in the repository | Present but at `deploy/docker-compose.yml`, not `compose.yml` |
| No published ports | **Violated** — `deploy/docker-compose.yml` publishes `8090:8080` |
| `<stack>-<role>` container name | **Violated** — `container_name: model-registry`; no role token, and a separator in the stack token |
| Networks `<stack>-<purpose>` | **Missing** — no `edge`, no `telemetry` |
| `.env.example` complete and committed | **Missing** |
| Healthcheck on every container | **Missing** from compose |
| `GET /health` reporting dependencies | Partial — `/healthz` exists and reports snapshot staleness, but the name differs from the checklist's `/health` |
| Migrations as one-shot | Not applicable — no database; the snapshot is a JSON file |
| Structured JSON logs, §7 fields | **Violated** — Serilog plain console, none of the five fields |
| `/metrics` in Prometheus format | Present, via prometheus-net |
| Container joined to `telemetry` | **Missing** |
| Third-party images pinned | Yes — `sdk:10.0`, `aspnet:10.0-alpine` |
| Actions workflow pushing to ghcr | Present but tag-triggered, `:latest`, no short sha |
| `CLAUDE.md` with a pointer to the guideline | Present as of 2026-08-22, but **carries no pointer** to the guideline |
| Retention written down | Not applicable — the snapshot is overwritten, nothing accumulates |

The estate's feed-metric contract is also unmet. §7 requires
`feed_last_success_timestamp_seconds{feed="…"}` and `feed_expected_interval_seconds{feed="…"}`
from anything ingesting on a schedule, so one alert rule shape works for every feed. This
service publishes `model_registry_source_last_success_seconds`, which is seconds-since rather
than a timestamp, under a name no alert rule in this estate looks for. §7 also requires that a
feed which has never succeeded publish no value rather than zero.

## Ruled out

- **Publishing a host port and pointing DNS at it** — §4 and §8; Docker's iptables rules bypass
  ufw, so a published port is a decision about the public internet even when it looks local.
- **Fronting the service with Varnish inbound** — Varnish is outbound-only in this estate and
  never appears in a Caddyfile; putting it in front of one of our own services goes through §9.
- **Serving the catalog from a consumer's own origin instead of its own hostname** — §4's
  browser rule is about pages fetching shared services, and this is a server-side API consumed
  by a NuGet client, plus a hostname already published in the README and in DNS.

## Still open

- **Whether the sync reads its three feeds direct or through Varnish** — carried into the
  spec's interview; deviating from the guideline's caching rule is a §9 act.
- **Whether the public read surface needs rate limiting, and at what cost** — carried into the
  spec's interview; the only Caddy-layer answer changes the shared proxy image.
- **Whether the telemetry collector should discover targets rather than name them** — carried
  into the spec's deferred register. It is a guideline/host discrepancy that predates this work
  and blocks nothing: the collector is edited either way.

## Settled 2026-08-22, after the first interview round

The three questions carried out of the findings above were put and answered. Recorded here so
the findings and the spec do not disagree; the reasoning and what each ruled out are in the
spec's `Decisions`.

- **Feed reads go direct, not through Varnish** — recorded as a §9 deviation rather than taken
  quietly. `vcl/default.vcl` is untouched by this work.
- **No rate limiting** on the public read surface. The `caddy-ratelimit` finding above stands as
  the reason the edge-layer answer was declined: it would replace the pinned image every stack
  in the estate sits behind.
- **Estate consumers call `modelcatalog-api` over `edge`**, not the public hostname.

The collector-discovery discrepancy is unchanged and stays in the spec's deferred register. It
predates this work and blocks nothing — the scrape list is edited by hand either way.

---

# Planning research, 2026-08-22

Appended by `/plan-work`. Everything above is the shaping research; this answers the
unknowns that came up while phasing the work.

## The stale-feed alert already covers this service — no new rule

Alerts are not configured in OpenObserve's UI. They live in
`~/ServerManagement/scripts/telemetry-alerts.sh`, which posts them to OpenObserve's API and is
idempotent: it deletes an alert of the same name before creating it, so editing a query there
and re-running is how one is changed. The script's own header says it exists because those
alerts otherwise live "in OpenObserve's own database, which is one volume and no backup."

Three feed-stale alerts, split by how often a feed publishes:

```
FAST_FEEDS="'crow', 'ovfiets', 'donkey', 'send_worker', 'chat_watcher'"
WEEKLY_FEEDS="'corpus'"
```

- `feed-stale-minutes` — `feed IN (FAST_FEEDS)`, 1800 s
- `feed-stale-daily` — `feed NOT IN (FAST_FEEDS) AND feed NOT IN (WEEKLY_FEEDS)`, 129600 s
- `feed-stale-weekly` — `feed IN (WEEKLY_FEEDS)`, 907200 s

**`feed-stale-daily` is a catch-all by exclusion**, and 129600 s is exactly 1.5 × 86400 — the
guideline's rule, hardcoded for a nightly feed. `~/ServerManagement/CLAUDE.md` states the
consequence directly: "a feed named in neither list falls to the 36-hour alert rather than to
nothing."

So `litellm`, `openrouter` and `modelsdev` are covered the moment they publish
`feed_last_success_timestamp_seconds`, with no edit to the alert script. That removes the
alert-authoring work the spec anticipated and leaves only the part §7 insists on: firing it
once on purpose, because "a query that is valid, enabled and matches nothing looks identical to
one that is working."

Source: `~/ServerManagement/scripts/telemetry-alerts.sh`, `~/ServerManagement/CLAUDE.md`, read
2026-08-22.

## The bind mount would silently fail to persist

The runtime image is `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` and `deploy/Dockerfile` ends
with `USER app`. On the running `assistant-web` container — the same base family — that user is
**uid 1654**:

```
$ docker exec assistant-web id
uid=1654(app) gid=1654(app) groups=1654(app)
```

PromptImprover's bind mount at `/srv/promptimprover/data` is `root:root 0755`, and that works
only because its container runs as **uid 0**:

```
$ docker exec promptimprover-app id
uid=0(root) gid=0(root)
```

A bind mount Docker creates for this stack would likewise be `root:root`, and uid 1654 could not
write to it. The failure is worse than a crash. `SnapshotStore.SwapAsync` assigns `_current`
**before** it opens the file, so the service would serve a correct catalog from memory, throw on
persistence, and only reveal the problem at the next restart — which would come up with no
snapshot at all and 503 every read until a sync finished. On every sync, from day one, invisibly.

**A named volume avoids it without a manual step.** Docker initialises a fresh named volume from
the image's contents *and ownership*, and `deploy/Dockerfile` already does
`RUN mkdir -p /app/data && chown -R app:app /app`, so the volume comes up owned by uid 1654. A
bind mount would need an `install -d -o 1654 -g 1654` that somebody has to remember on every
rebuild, and forgetting it fails in the silent way above.

**This revises the spec**, which drafted a bind mount under `${DATA_ROOT:-/srv}/modelcatalog/data`.
That was drafted prose rather than a logged decision — no `Decisions` entry covers it — so it is
changed here rather than treated as settled ground. The plan uses a named volume
`modelcatalog-data`, named explicitly per §5, which also matches what the assistant stack does.

Source: `docker exec` on this host, `deploy/Dockerfile`,
`src/ModelCatalog.Service/Catalog/SnapshotStore.cs`, `~/Development/PromptImprover/compose.yml`,
all 2026-08-22.

## Copying the estate's logging means dropping Serilog, not configuring it

`Assistant.Core/Observability/EstateLogging.cs` calls `logging.ClearProviders()` and registers a
`ConsoleFormatter` on `Microsoft.Extensions.Logging`:

```csharp
public static ILoggingBuilder AddEstateJsonLogging(
    this ILoggingBuilder logging, IConfiguration configuration, string service)
```

`JsonLogFormatter` writes the five fields and takes `event` from `logEntry.EventId.Name`, with
the logger category as a fallback. Its own comment explains why that matters: the event name
comes from the source-generated log method, "which is what makes 'never invent a second spelling
of an existing event' structural rather than a rule someone has to remember."

This fits `model-catalog` better than it might look. The service already uses
`LoggerMessage.Define` with **named** EventIds — `new EventId(1, "SourceFailed")`,
`new EventId(2, "SyncComplete")` in `SyncPipeline`, `new EventId(1, "StaleServed")` in the
client. Those names land in `event` unchanged. What they are not is §7's dotted spelling, so
they need renaming to stable identifiers of the `feed.poll.failed` shape.

Serilog then has no job left: it is configured in `Program.cs` only as
`.WriteTo.Console()`, which the formatter replaces. Keeping both would mean two logging
pipelines writing to the same stdout in two formats.

Source: `~/Development/NajsPersonalAssistants/src/Assistant.Core/Observability/`,
`src/ModelCatalog.Service/Program.cs`, `src/ModelCatalog.Service/Jobs/SyncPipeline.cs`, read
2026-08-22.

## The "no value, not zero" rule already holds structurally

§7 requires that a feed which has never succeeded publish no value rather than zero. In
`SyncPipeline.RunAsync` the per-source gauge is only touched inside a success test:

```csharp
if (r.State.LastSuccess is { } ls)
    MetricsRegistry.SourceLastSuccessSeconds.WithLabels(r.Name).Set(...);
```

prometheus-net creates a labelled child on the first `WithLabels` call, so a feed that has never
succeeded has no child and therefore no series. That property survives the rename as long as the
new gauge keeps being written inside the same test — worth knowing, because moving the write out
of the conditional to "initialise" the metric would quietly reintroduce the 1970 timestamp §7
warns about.

What does change is the value: seconds-since-success becomes an absolute Unix timestamp.

Source: `src/ModelCatalog.Service/Jobs/SyncPipeline.cs`,
`src/ModelCatalog.Service/Metrics/MetricsRegistry.cs`, read 2026-08-22.

## The collector entry has a fixed shape, and this service's port is unusual

Targets in the `services` scrape job carry two labels:

```yaml
- targets: [promptimprover-app:8000]
  labels: {service_name: promptimprover, service_role: app}
```

Most estate services listen on 8000. This one listens on **8080** — `deploy/Dockerfile` has
`EXPOSE 8080` and the existing compose sets `ASPNETCORE_URLS: http://+:8080`, which matches the
.NET default for these images. Mixed ports are already normal in that file (`:9464` for two
workers, `:9131` for the varnish exporter), so the entry is
`modelcatalog-api:8080` with `{service_name: modelcatalog, service_role: api}`.

Source: `~/ServerManagement/stacks/telemetry/otel-collector-config.yaml`, `deploy/Dockerfile`,
`deploy/docker-compose.yml`, read 2026-08-22.

## Ruled out

- **Configuring Serilog to emit the §7 fields** — it would be a second implementation of a
  contract the estate has already implemented once, and §7 says the setup is copied per service
  rather than reinvented. Cost of copying: a third copy to edit when the contract changes, which
  §7 accepts explicitly until that happens.
- **A bind mount for the snapshot** — see above; fails silently under uid 1654.
- **Writing a `feed-stale-modelcatalog` alert** — `feed-stale-daily` already covers a nightly
  feed at exactly the guideline's threshold. A fourth rule would be a second place the same
  question is asked.

## Still open

- **How `feed_expected_interval_seconds` stays true if `ModelRegistry:SyncCron` changes** —
  carried into Phase 1's `Settle first`. The cron is configurable and the interval is a constant
  86400 if it is hardcoded, so a schedule change would leave the alert reading a stale contract.
- **Whether `/healthz` is renamed to `/health`** — carried into Phase 1's `Settle first`. §6's
  checklist says `GET /health`; the spec keeps `/healthz` and marks the departure `[THIN]`. The
  README documents `/healthz` publicly, so a rename is a documented-surface change.
- **Whether the old `ghcr.io/pinkroosterai/model-catalog` tags are deleted** — carried into
  Phase 2's `Settle first`; it is the spec's deferred row about the README's self-host path.
- **Whether `ACME_EMAIL` is set on the Caddy stack** — carried into Phase 4's `Settle first`,
  from the spec's deferred register. Estate-wide, not this service's to decide alone.

---

# Execution findings

## Phase 1 settled: the interval metric derives from the cron, 2026-08-22

`feed_expected_interval_seconds` must not be a hardcoded 86400. `ModelRegistry:SyncCron` is
configurable, so a hardcoded constant becomes a lie the moment the schedule moves — and the
alert reads that value as the feed's contract, which is precisely §7's warning about "a feed
that starts running less often becomes a rule nobody updates."

Quartz is already a dependency and parses the expression the scheduler itself uses.
`CronExpression.GetNextValidTimeAfter(DateTimeOffset)` is present in Quartz 3.19.1 — confirmed
against the resolved assembly at
`~/.nuget/packages/quartz/3.19.1/lib/net8.0/Quartz.dll`. The gap between the next two valid
fire times is the interval, and for `0 0 1 * * ?` that is exactly 86400.

The caveat, worth knowing before someone changes the cron: for an irregular schedule — weekdays
only, say — consecutive gaps differ, so the published value is the *next* interval rather than a
constant. That is still better than a hardcoded number, because it tracks the expression instead
of contradicting it.

Source: resolved Quartz assembly on this machine, 2026-08-22.

## Phase 1 settled: `/health` is added, `/healthz` is kept — the image is published

The plan asked whether `/healthz` gets renamed to `/health` per §6's checklist. The answer turns
on a fact neither the spec nor the plan had: **this project has already shipped.**

- `git tag` shows `v0.1.0` (2026-04-14) and `v0.2.0` (2026-04-18), so the tag-triggered release
  workflow has run twice.
- `ghcr.io/pinkroosterai/model-catalog` exists as a published container package.
- `ModelCatalog.Client` is on nuget.org at 0.1.0 and 0.2.0 —
  `https://api.nuget.org/v3-flatcontainer/modelcatalog.client/index.json` lists both.

So `/healthz` is a documented endpoint on a publicly pullable image, and a self-hoster's compose
healthcheck may well be pointing at it. Renaming it outright would break that silently, four
months after the fact, to satisfy a naming preference.

**Both are served.** `/health` is added as the estate-contract endpoint and reports the three
feeds by name rather than a bare status, which is the half of §6 the current endpoint actually
fails. `/healthz` stays as an alias over the same handler. The cost is one extra route mapping;
the cost of the alternative is a broken healthcheck somebody debugs from the outside.

This supersedes the spec's `[THIN]` note on `/healthz` staying public, which assumed the only
question was the name.

Source: `git tag`, `gh api /user/packages`, nuget.org flat container index, all 2026-08-22.

## Phase 1 settled: the container probe and the HTTP endpoint answer different questions

Related, and worth separating before the healthcheck is written. `/health` returns 503 when the
snapshot is past `StaleThresholdHours` — correct for an HTTP caller deciding whether to trust
the answer.

The **container** healthcheck must not use that semantics. A catalog three days stale is still
serving correct-if-old data, and marking the container unhealthy for it duplicates the
`feed-stale-daily` alert while telling the operator something misleading. The probe fails only
when the process cannot serve at all — no snapshot in memory.

This matches the estate precedent: the assistant's `--health` is a CLI flag reading the
process's own state, not an HTTP call to itself.

Source: `spec.md § Qualities and constraints` ("correctness over freshness"),
`~/Development/NajsPersonalAssistants/compose.yml`, 2026-08-22.

## Phase 1 found: the CI formatting gate has never worked, 2026-08-22

`.github/workflows/ci.yml` installs `CSharpier --version 1.2.*` and then runs
`dotnet csharpier check src tests`. CSharpier 1.x ships a `csharpier` binary and no
`dotnet-csharpier` shim, so that command cannot resolve — verified by installing 1.2.6 locally,
which reports "You can invoke the tool using the following command: csharpier" and leaves
`dotnet csharpier --version` failing.

`gh run list` shows every CI run failing since 2026-04-14, each in about 30 seconds, and
`gh run view 32564159520 --log-failed` names the step: "Could not execute because the specified
command or file was not found." The step runs *before* `dotnet build` and `dotnet test`, so the
suite has not executed in CI for four months. Only the tag-triggered Release workflow, which does
not invoke CSharpier, has been green.

The fix is one word — `csharpier check src tests` — and belongs to Phase 2, which rewrites the
workflows. Worth stating plainly because Phase 2's `Done when` says the image build is "gated on
the tests": until this is repaired, that gate does not exist.

Source: `gh run list`, `gh run view 32564159520 --log-failed`, local `dotnet tool install`, all
2026-08-22.

## Phase 2 settled: the old image keeps being published as an alias, 2026-08-22

`gh api /user/packages/container/model-catalog` reports `"visibility":"public"`, created
2026-04-14, two versions carrying `v0.1.0`, `v0.2.0` and `latest`. The README that tells people
to `docker run ghcr.io/pinkroosterai/model-catalog:latest` ships inside `ModelCatalog.Client`,
which is public on nuget.org. So the instruction is genuinely distributed and the image is
genuinely pullable by anyone.

That rules out both simple answers. **Deleting** the package breaks those pulls — and could not be
done from here anyway, the local token holding `read:packages` but not `delete:packages`.
**Freezing** it is worse in the way this estate dislikes most: a self-hoster keeps pulling
`:latest`, keeps getting April code, and nothing tells them. A stale artifact that still answers
is the silent failure, not the loud one.

**So CI pushes both names from the same build.** `ghcr.io/pinkroosterai/modelcatalog` is
canonical and what the estate pulls, per §5's `ghcr.io/pinkroosterai/<stack>`;
`ghcr.io/pinkroosterai/model-catalog` continues to receive the same digests so nobody is stranded.
It costs one extra tag pair on a build that already happened — no extra build time — and two
package paths to look at instead of one. The README is corrected in Phase 4 to name the canonical
path and mark the other legacy.

Source: `gh api /user/packages/container/model-catalog`, `gh auth status`, 2026-08-22.

## Phase 2 revised: the formatter is pinned in a repo tool manifest, 2026-08-22

The estate's .NET tooling note (`~/ServerManagement/docs/dotnet-tooling.md`) states the rule
directly: a repository that enforces formatting or analysis "writes a committed
`.config/dotnet-tools.json`, and CI's `dotnet tool restore` gets the same version everyone else
has. The host copies float — `--update` moves them whenever it is run — and a formatter one
version ahead of CI's reformats the whole repo on the next commit."

That is not hypothetical here. This machine's global CSharpier was 1.3.0 while `ci.yml` pinned
`1.2.*`; it had to be uninstalled and reinstalled by hand to format this repo the way CI would.
A manifest makes that structural instead of remembered.

It also explains the original failure rather than merely fixing it. `dotnet csharpier` is the
*local-tool* invocation form and is correct — paired with a manifest. The workflow paired it with
a **global** install, where CSharpier 1.x provides only a `csharpier` binary and no `dotnet-`
shim. So the command was right and the install was wrong, which is why it reads as a working step.

`.config/dotnet-tools.json` pins 1.2.6 and CI now runs `dotnet tool restore` before
`dotnet csharpier check src tests`. Note `dotnet new tool-manifest` wrote the file to the
repository root here; it was moved to `.config/`, which is the location the estate note names and
the one the tooling searches by default.

Source: `~/ServerManagement/docs/dotnet-tooling.md`, local `dotnet tool` runs, 2026-08-22.

## Phase 3 found: a scaffolded `.env` opened refresh and then crashed the container, 2026-08-22

Two defects in the `.env.example` written in Phase 2, both found before anything was deployed and
both caused by the same §5 instruction — "every key present, no values."

**An empty key is not an absent key, and it authorised refresh.** `ApiKeyEntry.Key` defaults to
`string.Empty`, and `RefreshEndpoints` guarded only on `configured.Count == 0`. A `.env` copied
from the example sets `ModelRegistry__ApiKeys__0__Key=`, which produces an entry that *exists* —
so the count is 1, refresh is "enabled", and `string.Equals(k.Key, provided)` matches any request
sending `X-Api-Key:` with an empty value. Fixed by filtering blank keys before the count, so an
unfinished `.env` leaves refresh disabled rather than open. `RefreshKeyConfigTests` covers it, and
was confirmed to fail against the unfixed code rather than merely passing against the fixed one.

**Empty optional values crash the container at startup.** Configuration binding converts what it
finds, and `""` is found. A verbatim copy died with `Failed to convert configuration value '' at
'ModelRegistry:StaleThresholdHours' to type 'System.Int32'` before reaching the scheduler — and
`ModelRegistry__SyncCron=` would have failed next, an empty string not being a valid Quartz
expression. With `restart: unless-stopped` that is a crash loop, from a file the estate's own
convention told the operator to copy.

The optional overrides are now commented out in the example with their defaults shown, which
keeps them documented and present while making a verbatim copy boot. The credential keys stay
uncommented because they are what an operator actually fills in, and the code fix above makes
leaving them blank safe. Verified: a verbatim scaffold now reaches "Application started" with
zero unhandled exceptions.

Worth carrying to any other stack written from this template — §5's "no values" is right for a
secret and wrong for a typed optional.

Source: local runs against the built service, 2026-08-22.

## Phase 4 raised, not decided: `ACME_EMAIL` on the Caddy stack, 2026-08-22

The spec deferred this and it stays deferred, because it is estate-wide rather than this
service's to settle. The Caddyfile carries it as a commented recommendation:

```
# Recommended once ACME_EMAIL is set in .env — Let's Encrypt then sends
# expiry and problem notices instead of failing silently.
```

Unset, a renewal failure is silent for **every** site on this host, not just this one. This
service's certificate was issued 2026-08-22 and expires 2026-11-20, so nothing is urgent, and a
change to the shared proxy's `.env` on behalf of one service is the kind of quiet estate-wide
edit §9 exists to prevent. Left for the operator.

## Phase 4 verified: the stale-feed alert fires, 2026-08-22

Two separate things had to be true, and each was checked on its own.

**The rule's scope.** `feed-stale-daily` matches by exclusion, so nothing names this service and
a label collision would silently move a feed into the wrong window. Running the shipped rule's
exact `WHERE` clause against OpenObserve returns `litellm`, `modelsdev`, `neushoorn`,
`openrouter` — the three new feeds are in scope, and none collides with `FAST_FEEDS` or
`WEEKLY_FEEDS`.

**Delivery.** A temporary alert was created with the shipped SQL **verbatim except the time
constant** — `now` in place of `now - 129600`, since the honest alternative is waiting 36 hours —
at a one-minute frequency, against the real `ntfy` destination and template. It fired, and the
notification was read back off the topic: title `feed-stale-daily-firetest`, body naming the
stream. Changing only the constant is what makes this a real test: the failure §7 warns about is
a query that can never match — a wrong label, a wrong stream, a typo — and that is exactly what
holding the rest of the query fixed exercises.

The temporary alert was deleted and `scripts/telemetry-alerts.sh` re-run, leaving the nine alerts
the script defines and nothing else. A `container-restarted` alert also arrived unprompted during
this work, tripped by the deploy's own restarts — incidental confirmation that the estate's
alerting path was already live.

Source: OpenObserve API and `ntfy.sh` topic poll on this host, 2026-08-22.

## Close: security review of the change range, 2026-08-22

The `/security-review` harness diffs a feature branch against `main` and this work *is* on `main`,
so it received an empty diff and had nothing to analyse. Reviewed inline over `a5c6b8a..HEAD`
instead. Three pieces of genuinely new attack surface, none of them a finding:

- **`RequestIdMiddleware` reflects an attacker-controlled header.** Tested rather than reasoned
  about: `X-Request-Id: abc\r\nX-Injected: pwned` through Caddy comes back as `x-request-id: abc`
  with no injected header, and the response is HTTP/2, where response splitting is structurally
  impossible. A 4000-character value is reflected and answered 200. The value also reaches the
  logs, but it is written through `Utf8JsonWriter`, which escapes it.
- **`/health` exposes each feed's `lastError`.** Not new surface: `/v1/meta` already returned the
  same `SourceStates` before this work, and it is public too. The strings are exception messages
  from fetches of three documented public URLs.
- **The refresh auth path changed** — and the change closes a hole rather than opening one; see
  `§ Phase 3 found` above. The comparison is still `string.Equals` rather than constant-time,
  which is pre-existing and not worth a finding on an endpoint that is 404'd at the edge.

Not examined here because they are excluded from that review's scope: secrets at rest (`.env` at
mode 600), and the absence of rate limiting, which is a recorded decision in the spec.

## Reversed the same day: only one image name, 2026-08-22

`§ Phase 2 settled` above concluded that CI should publish `modelcatalog` and `model-catalog` from
one build, so that anyone following the April README kept working and kept updating. **The user
overruled it.** Only `ghcr.io/pinkroosterai/modelcatalog` exists; the old package is deleted and
the name is gone from the workflow, the README and `CLAUDE.md`.

What that costs, stated once and not re-argued: the package was public and the instruction to
`docker run` it shipped inside `ModelCatalog.Client` on nuget.org, so a self-hoster's next pull
fails with manifest-unknown rather than resolving anywhere. There is no redirect on ghcr — a
deleted name is simply gone. Anyone affected has to read the current README.

What it buys: §5's one image path per stack, with nothing to explain and no second package to
keep alive. The earlier finding stands as the record of what was weighed.

Deleting the package needs a `delete:packages` scope the working token does not carry.

