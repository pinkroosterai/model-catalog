# Spec: models.pinkrooster.nl

**In one sentence** — Put the ModelCatalog service behind Caddy on `models.pinkrooster.nl` as a
public, read-only API, built to the estate's operational contract so it can be seen, alerted on
and rolled back like every other stack here.
**Status** — buildable
**Coverage** — 5/9 clear · 4 partial · 0 missing
**Research** — `research.md`

## Outcome

`https://models.pinkrooster.nl` answers questions about LLM models — what they cost, how much
context they take, what they can do — to anyone who asks, without a key. The .NET client the
repository already publishes points at it and works. When an upstream feed changes shape or
goes away, somebody finds out from an alert rather than from a wrong number in a bill.

The DNS record already points here and the README already advertises the endpoint, so the
externally visible half of this is a promise the estate has made and not yet kept.

## Who it is for, and what they do with it

**Estate services that need model metadata.** The reason the catalog exists. They take a
dependency on the `ModelCatalog.Client` NuGet package, call `GetModelAsync`, and use the
`Pricing` record to work out what a call cost. `promptimprover` and the `assistant` stack are
the obvious candidates — the assistant's `/metrics` already tracks token spend, which is the
number this catalog makes convertible into money. [THIN: no consumer has been confirmed; this
is inference from what those services do, not from a request either of them made]

**Anyone on the internet**, reading the same endpoints unauthenticated — the case the README
sells. Low-volume by intent, with self-hosting offered as the answer to anyone wanting more.

**The operator**, who deploys it, watches whether the three feeds are still arriving, and holds
the only credential in the system: the API key on `POST /v1/refresh`.

**Estate consumers take the short path.** A service on this host sets `BaseUrl` to
`http://modelcatalog-api:8080` and reaches the container directly over `edge`, skipping Caddy,
TLS and the public door. It is one hop, and it keeps working when Caddy does not. The cost is
two `BaseUrl` values in the estate — the internal one and the public one the README documents —
and a public path carrying little real traffic, so a bug on it surfaces late.

## Scope

**In**

- A Caddy site block for `models.pinkrooster.nl`, TLS provisioned automatically.
- `compose.yml` at the repository root on the estate's shape: no published ports, explicit
  container and network names, `edge` + `telemetry`, healthcheck, log rotation and labels.
- The operational gaps that make the service invisible or unrollbackable: §7 JSON logging, the
  §7 feed-metric pair, short-sha image tags, `.env.example`.
- Editing `~/ServerManagement` — the Caddyfile, and the collector's static scrape list.
- A stale-feed alert, fired once on purpose before it is trusted.
- A §9 row in `~/ArchitectureRedesign/docs/open-questions.md` recording the deviation from §4's
  third-party caching rule, and the guideline edit that follows from resolving it.

**Out**

- Any change to what the catalog *contains* or how it merges. Sources, normalizers, the alias
  map and `PriorityMerger` are untouched; this is a deployment, not a feature.
- Historical pricing. The snapshot is overwritten and nothing accumulates, which is why the
  §6 retention line does not apply.
- A second environment. There is no staging here; a dev stack would be `dev-modelcatalog-*` and
  off `edge`, and nothing has asked for one.
- Rate limiting, in this service or at the edge. Decided against, with the exposure that leaves
  written down under `Qualities and constraints` rather than left implicit.
- Publishing the NuGet package. Already solved by `release.yml` and orthogonal to running the
  service.

## How it works

**Naming.** The stack token is `modelcatalog` — one lowercase word, no separators, per §5. The
one container is `modelcatalog-api`, `api` being the role from §5's fixed vocabulary. This
replaces `model-registry` in the current compose file, which has a separator and no role.

The image moves with it: `ghcr.io/pinkroosterai/modelcatalog`, not the repository-derived
`model-catalog` the release workflow pushes today. [THIN: nothing says whether the existing
`ghcr.io/pinkroosterai/model-catalog` tags are left in place or cleaned up, and the README's
self-host instructions name the old path]

**Reaching it.** A site block in `~/ServerManagement/stacks/caddy/Caddyfile`, `import
accesslog`, `reverse_proxy modelcatalog-api:8080`. Not `accesslog_noquery`: this service's
query parameters are `?provider=` and `?modality=` filters, which carry nothing about the
visitor. Caddy provisions the certificate on reload because DNS already points here.

`/metrics` and `/v1/refresh` are both 404'd at the edge, following `wa`, `assistant` and
`mail`. The collector reaches `/metrics` over `telemetry`; refresh is triggered with
`docker exec`. `/healthz` stays public — it is the endpoint the README documents
and it reveals only snapshot age. [THIN: the three precedents 404 their health endpoint too;
this departs from them and the reason is one sentence long]

**Running it.** One container, no database. The snapshot is a JSON file on a bind mount under
`${DATA_ROOT:-/srv}/modelcatalog/data`, which is what lets a restart serve yesterday's catalog
immediately rather than nothing — `SnapshotStore.TryLoadFromDiskAsync` runs before any endpoint
is mapped. Networks are `edge` and `telemetry`; there is no `modelcatalog-internal` because
there is no second container to talk to.

Healthcheck asks the app rather than curl, which the runtime image does not carry.

**Being seen.** Serilog is reconfigured to emit the §7 contract — `ts`, `level`, `service`,
`event`, `request_id` — as JSON to stdout. The assistant stack has already solved this in .NET
in `Assistant.Core/Observability/`, and §7 says this setup is copied per service rather than
imported, so it is copied. Event names are stable identifiers: `sync.completed`,
`sync.source.failed`, `refresh.rejected`.

The feed metrics are renamed to the estate's pair, per feed, one feed per source:

```
feed_last_success_timestamp_seconds{feed="litellm"}
feed_expected_interval_seconds{feed="litellm"}
```

This replaces `model_registry_source_last_success_seconds`, which is seconds-since rather than
a timestamp and sits under a name no alert rule here looks for. A source that has never
succeeded publishes no value, not zero — zero is 1970, which reads as silent forever.
`feed_expected_interval_seconds` is 86400, from the daily cron.

The other `model_registry_*` metrics keep their names. They are this service's own business
and nothing estate-wide reads them. [THIN: nothing decided about whether the `model_registry_`
prefix should follow the rename to `modelcatalog_`, or what that would cost a dashboard]

**Shipping it.** The workflow moves to the estate's shape: build on push to `main`, gated on
`dotnet test`, pushing `:<git-short-sha>` and `:current`. Rollback is `IMAGE_TAG` in `.env` and
`docker compose up -d`. The existing tag-triggered NuGet job stays as it is — the package and
the image have genuinely different release cadences, and the package is versioned by hand.

**Feeding it.** The daily sync reads its three third-party documents directly, not through
`varnish-cache`. §4 sends slow-moving third-party metadata to the outbound cache, and this is a
deliberate deviation rather than an oversight: three documents fetched once a day get nothing
from a cache a direct read does not already have, and none of the three feeds needs a
credential — so the rule's two purposes, collapsing repeated reads and holding upstream keys in
one place, both come out empty here. `vcl/default.vcl` is untouched.

Deviating is a §9 act and is recorded as one: a row in
`~/ArchitectureRedesign/docs/open-questions.md` naming what was done, why the rule blocked it,
and what it costs either way. Going direct costs two things worth writing next to the decision —
a feed outage is this service's problem alone with no cached copy to serve through it, and this
stack's third-party reads are logged nowhere, the cache being the only place they otherwise
would be.

## Domain and terms

| Term | Means here |
|---|---|
| **stack** | `modelcatalog`. The compose project, the log label, the ghcr image path. |
| **container** | `modelcatalog-api`. The only one. |
| **feed** | One upstream — `litellm`, `openrouter`, `modelsdev`. The label on the §7 feed metrics. Called a *source* inside the codebase, and `ISource`/`SourceState` are not renamed; **feed** is the operational word and appears only in metrics and alerts. |
| **snapshot** | The merged catalog: one `NormalizedSnapshot`, held in memory and persisted to `snapshot.json`. |
| **staleness** | Age of the snapshot's `FetchedAt`. Past `StaleThresholdHours` (72) `/healthz` reports degraded and returns 503. |
| **sync** | One run of `SyncPipeline` across all feeds. Daily at 01:00 UTC, or on `POST /v1/refresh`. |
| **degraded** | Serving a snapshot older than the stale threshold. Distinct from *down*: the answers are still there and still probably right. |

One thing this table settles: the estate word is `modelcatalog` everywhere operational, while
the code keeps saying `ModelRegistry` in its config keys and `model_registry_` in its metric
names. That split is deliberate and recorded in `CLAUDE.md` — the config keys are what a
running `.env` already sets.

## Qualities and constraints [THIN: no availability target, no traffic estimate, and no budget for what an unauthenticated public endpoint may cost]

**Constraints, which are firm.** Everything in the architecture guideline: no published port,
Caddy as the only public door, secrets in `.env` at 600, JSON logs, `/metrics` on `telemetry`,
images built in CI and never on the server, third-party images pinned. The service must keep
answering reads while a sync is running and while any subset of feeds is failing — that is
already how `SyncPipeline` behaves and the deployment must not undo it.

**Qualities, which are guesses.**

- *Availability* — best-effort. It is one container on one host with no replica; a restart is a
  few seconds of connection refused, and Caddy will 502 through it.
- *Correctness over freshness* — a stale catalog is much better than a wrong one. Everything
  about the design already says this: keep the last snapshot when every feed fails, don't
  advance `FetchedAt`, serve from disk on boot.
- *Public read cost* — the read path is a `FirstOrDefault` over an in-memory list per request,
  so it is cheap until the list is large or the traffic is. **No rate limiting ships with this
  deployment**, on the same reading veilchat went public under: decided, not overlooked. Caddy
  has no built-in limiter, and the only edge-layer answer would rebuild the pinned image every
  stack in the estate sits behind in order to protect one service. What that leaves standing:
  nothing throttles a scraper, and the hostname becomes discoverable through certificate
  transparency the moment the certificate issues. The access log and the request metrics are how
  this gets noticed, which makes them the thing to look at before assuming the traffic is fine.
- *The refresh key* — one secret, and it never faces the internet. `POST /v1/refresh` is 404'd
  at the edge alongside `/metrics`, so the estate's only public credential stays the mail
  stack's, which is where §8 puts it. The key stays configured and the in-process check stays as
  defence in depth against anything already on `edge`. Forcing a refresh means `docker exec` on
  the host: a shell for the operator, and that is the whole of the downside.

## Depends on

| Thing | Needed for | When it is unavailable |
|---|---|---|
| `caddy` stack | Public reach | The hostname stops answering. Estate consumers are unaffected — they call `modelcatalog-api` over `edge` and never touch Caddy. |
| `edge` network | Public reach *and* estate consumers | Nothing reaches the service at all. This is the single point of failure the internal path buys its extra hop back from. |
| DNS `models.pinkrooster.nl` → `95.216.224.106` | TLS issuance and reach | Already correct. If it changed, Caddy could not renew and the site would fail closed. |
| Let's Encrypt | The certificate | Existing cert serves until expiry; a renewal failure is silent unless `ACME_EMAIL` is set, which it is not — the Caddyfile carries that as a commented recommendation. |
| `telemetry` network + collector | Metrics, and therefore alerts | The service runs and serves normally; nobody can see it. A stale feed becomes invisible, which is the failure this whole section exists to prevent. |
| `~/ServerManagement` repo | Caddyfile block, collector scrape entry | The deployment is not complete without commits in a second repository. |
| ghcr.io | Pulling the image | Deploy and rollback both block. The running container is unaffected. |
| LiteLLM raw GitHub JSON | A third of the catalog | Sync degrades: other feeds succeed, this one's `LastError` surfaces in `/v1/meta`, previous values for its exclusive models persist. |
| OpenRouter API | Pricing, mainly — it is first in `PricingOrder` | Same degradation. Prices fall through to LiteLLM, so numbers change without the catalog saying they became less trusted. |
| models.dev | Display names, mainly | Same degradation, least visible impact. |
| `varnish-cache` | Only if the sync is routed through it | Unset cache URL means direct reads, which the guideline names as the rollback needing no code change. Moot until the blocking question above is answered. |

Nothing here owns data another estate service owns, and nothing reads another service's
database. §3 is satisfied trivially: the catalog's domain is merged LLM model metadata and it
is the only writer.

## Edge cases and failure modes

- **Cold start with no snapshot on disk.** Read endpoints return 503 with "Snapshot not yet
  available" until the startup sync finishes; `/healthz` returns 503 too. The container is
  healthy-but-empty for the length of one sync. The healthcheck `start_period` must exceed
  that or Docker restarts the container mid-sync, forever. [THIN: no measured figure for how
  long a cold sync takes — three HTTP fetches plus a merge, but it has not been timed here]
- **Every feed fails.** The previous snapshot is kept, `FetchedAt` does not advance, and the
  catalog quietly ages. This is the failure §7 warns is invisible — "a service cheerfully
  serving numbers that stopped updating three days ago" — and it is exactly what the stale-feed
  alert is for.
- **One feed fails.** Merge proceeds without it. A model only that feed knew keeps its old
  values; a field it won the priority order for silently falls through to the next source. The
  answer changes and nothing in the response says so. `Sources` on the record is the only
  signal, and only if a caller reads it.
- **Sync runs past midnight into the next cron.** Quartz's `[DisallowConcurrentExecution]` plus
  the static `Interlocked` flag shared with the refresh endpoint means the second run is
  skipped, not queued.
- **Refresh while a sync is running.** 409.
- **Refresh with no keys configured.** 503, not 401 — "disabled", not "wrong key".
- **Deploy during a sync.** The container stops mid-fetch; the snapshot on disk is whatever the
  last completed `SwapAsync` wrote, and that write is `.tmp` plus an atomic rename, so a
  half-written snapshot is not a state that exists.
- **Disk full.** `SwapAsync` throws after the in-memory snapshot has already been replaced, so
  the service serves a snapshot it failed to persist and a restart silently rewinds to the last
  one that landed. [THIN: no decision on whether this is worth handling or accepting]
- **Certificate renewal failure.** Fails closed, and with no `ACME_EMAIL` set there is no
  notice. Worth an alert or worth the email; neither exists.
- **A feed changes shape.** The normalizer produces fewer models or drops fields rather than
  throwing. `model_registry_models_total` moving sharply is the signal. [THIN: no alert on a
  sudden drop in model count, which is the shape this failure actually takes]

## Success criteria

1. `curl https://models.pinkrooster.nl/v1/models/openai/gpt-4o` returns 200 with a populated
   `pricing` object, from a machine outside this host.
2. The certificate is issued by Let's Encrypt and valid; no TLS error from a stock client.
3. `docker ps` shows `modelcatalog-api` healthy, and `docker inspect` shows no published port.
4. `curl https://models.pinkrooster.nl/metrics` returns 404, while the collector shows the
   service's metrics in OpenObserve.
5. `docker logs modelcatalog-api` emits JSON lines carrying `ts`, `level`, `service`, `event`
   and `request_id`, with `service` reading `modelcatalog`.
6. `feed_last_success_timestamp_seconds` is present for all three feeds and
   `feed_expected_interval_seconds` reads 86400 for each.
7. The stale-feed alert has been fired deliberately once — by holding a feed's clock or pointing
   it at a dead URL — and arrived on the ntfy topic. A rule that has never fired is
   indistinguishable from one that cannot.
8. Setting `IMAGE_TAG` to the previous short sha and running `docker compose up -d` rolls back,
   and the rolled-back container serves the same snapshot from disk.
9. Restarting the container serves the previous catalog without waiting for a sync — `/healthz`
   reports a snapshot age older than the process uptime.
10. `.env.example` is committed and complete, and a stack brought up from a clean checkout plus
    the example plus values reaches the same state.
11. `curl -X POST https://models.pinkrooster.nl/v1/refresh` returns 404, while a refresh
    triggered inside the container returns 202 and produces a new snapshot.
12. From another container on `edge`, `curl http://modelcatalog-api:8080/v1/meta` returns 200 —
    the path estate consumers actually take.

## Deferred questions

- **Should the telemetry collector discover scrape targets instead of naming them?** — would
  change: whether every new service needs a commit in `~/ServerManagement`, and would close the
  gap between §7's text and the running config. Blocked by it: nothing — the collector is edited
  by hand either way for this deployment.
- **Should `ACME_EMAIL` be set on the Caddy stack?** — would change: whether a renewal failure is
  noticed before the certificate expires, for every site on this host, not just this one.
  Blocked by it: nothing.
- **Does the `model_registry_` metric prefix follow the rename to `modelcatalog_`?** — would
  change: any dashboard or saved query naming those series. Blocked by it: nothing; the two
  estate-contract feed metrics are new names regardless.
- **Should the README's self-host instructions and public-endpoint claim be corrected in this
  work or after it?** — would change: whether the repository briefly documents an image path
  (`model-catalog`) that CI no longer pushes. Blocked by it: nothing.
- **Does a second consumer justify a shared `najs-logging` package?** — would change: whether
  the §7 formatter is copied a third time. Blocked by it: nothing; §7 says copy until the
  contract changes and three copies need editing together.

## Decisions

### 2026-08-22

- Derived, not asked: stack token `modelcatalog`, container `modelcatalog-api` — ruled out:
  keeping `model-registry`, which §5 forbids on two counts (separator in the stack token, no
  role token).
- Derived, not asked: `/metrics` 404'd at the edge — ruled out: publishing it, which would put
  per-feed success timestamps and model counts on the public internet for no caller's benefit.
  Follows `wa`, `assistant` and `mail`.
- Derived, not asked: feed metrics renamed to §7's `feed_last_success_timestamp_seconds` /
  `feed_expected_interval_seconds` pair — ruled out: keeping
  `model_registry_source_last_success_seconds` and writing a bespoke alert rule, which is the
  thing §7 says produces rules nobody updates.
- Q: How do estate services reach the catalog — over `edge`, or out through the public
  hostname? → A: "Direct over edge" — ruled out: a single `BaseUrl` for every caller, and with
  it the continuous exercise of the public path and of the client's stale-grace fallback. Accepts
  two `BaseUrl` values in the estate and a public path that stays lightly travelled.
- Q: Do the three feeds get read direct, or through `varnish-cache` as §4 says? → A: "Direct,
  recorded as a §9 deviation" — ruled out: three VCL routes with TTL and grace values for
  documents fetched once a day, and with them the cache's role as the one place third-party
  reads are logged. Commits this work to writing the §9 row rather than deviating quietly.
- Q: Rate limiting on the public unauthenticated read surface? → A: "Nothing now, watch the
  metrics" — ruled out: an in-process limiter (a feature added to a service this work was meant
  not to change) and an `xcaddy` build of `caddy-ratelimit` (rebuilding the image every stack in
  the estate sits behind, for one service). Accepts that nothing throttles a scraper.
- Q: Is `POST /v1/refresh` routed publicly, or blocked at the edge? → A: "404 at the edge,
  refresh via docker exec" — ruled out: routing it key-gated, which would put the estate's only
  public credential outside the mail stack on an endpoint with no ban-on-failure behind it. Also
  ruled out dropping the key entirely, which would leave no way to force a refresh short of a
  restart. Costs the operator a shell.

### 2026-08-22 — what execution changed

Appended by `/execute-plan`. The sections above are left as they were written; these are the
places the build overturned them.

- **The snapshot is on a named volume, not the bind mount `How it works` describes.** That line
  was drafted prose rather than a logged decision. The image runs as uid 1654 and a
  Docker-created bind mount is root-owned, so it would have been unwritable — and because
  `SnapshotStore` assigns the in-memory snapshot before it opens the file, the service would have
  served a correct catalog, persisted nothing, and revealed it only at the next restart by coming
  up empty. Confirmed on the host after deploying: `/app/data` is `1654 1654` with a 16 MB
  snapshot in it. Ruled out: a bind mount plus a documented `install -d -o 1654`, which fails
  silently the first time somebody forgets it.
- **`/health` was added beside `/healthz` rather than replacing it, and the `[THIN]` note on that
  paragraph is answered by a fact the spec did not have.** This project shipped in April: tags
  `v0.1.0` and `v0.2.0`, a **public** `ghcr.io/pinkroosterai/model-catalog` package, and
  `ModelCatalog.Client` 0.1.0/0.2.0 on nuget.org whose README tells people to run that image. So
  `/healthz` is a live surface with possible self-hosters, not a name nobody depends on. The two
  now answer different questions — `/healthz` is "should you trust this data" (503 when stale),
  `/health` is "is this container serving" (503 only with no snapshot) — and the container probe
  uses the second.
- **The old image name is retired.** ~~CI pushes both names from one build.~~ **Reversed
  2026-08-22, same day, by the user:** only `ghcr.io/pinkroosterai/modelcatalog` exists, the old
  package is deleted, and the name appears nowhere. The compatibility cost was put and declined —
  a self-hoster following the April README will get a manifest-unknown error on their next pull
  rather than a redirect or a stale image. §5 wants one image path per stack and now there is one.
- **Success criterion 9 could not be checked as worded.** "A snapshot age older than the process
  uptime" is invisible on a normal restart, because `RunSyncOnStartup` defaults to true and the
  sync finishes in about a second, making `FetchedAt` newer than the process either way. The
  property is real and was proven with the startup sync disabled: snapshot age 24s against 2.5s
  of uptime, 51 anthropic models served, zero sync runs. A future revision of this spec should
  reword the criterion rather than the behaviour.
- **`Qualities and constraints` gained a fact it was `[THIN]` for.** A cold sync measured 1.17s
  on this host, bounded near 30s in the worst case by the per-source timeout. That is what sets
  the healthcheck `start_period`, and the section had no measured figure when it was written.

Not overturned, and worth saying: no consumer has still been confirmed. `Who it is for` remains
`[THIN]` on exactly the point it was drafted `[THIN]` on — the estate-internal path is built and
verified, but nothing yet calls it.

