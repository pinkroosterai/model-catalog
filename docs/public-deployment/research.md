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
