# Flare

**A self-hosted, OpenTelemetry-native log dashboard for .NET developers — with a UI that doesn't feel like it's from 2014.**

Flare is an open-source log ingestion server and dashboard. Point any OTLP-capable logger at it and get a fast, modern, genuinely nice place to search, filter, and live-tail your structured logs. Self-hosted, low-overhead, and designed to be running in one command.

> Status: **v1 complete (2026-08-07).** Every item in the v1 roadmap below is built and e2e-verified — `docker compose up` gets you the full stack. This document remains the design contract; "Later" is next, gated on real usage per the scope-discipline principle below.

---

## Why this exists

The self-hosted log tooling landscape for .NET devs is a choice between two frustrations:

- **Seq** — solid engine, but the UI is dated and the free tier is single-user.
- **Grafana / SigNoz / OpenObserve** — powerful, OTel-native, but heavy to run, OTel-generic (not tuned to the .NET dev experience), and their dashboards are built for infra/SRE teams, not for a developer who just wants to read their app's logs beautifully.

Flare's bet: **the storage/query problem is already solved by ClickHouse. The differentiator is a great dashboard and a two-minute setup.** That's where the effort goes.

Flare is deliberately **not** trying to be a full observability suite (metrics, traces, APM, SIEM). It does logs, and aims to do them better-looking and lower-friction than anything else you can self-host.

---

## Design principles

1. **One protocol in: OTLP.** We don't write four ingestion adapters. Every supported logging library reaches Flare through the OpenTelemetry Protocol, using packages that already exist and are already maintained. This also means *any* OTLP source — including non-.NET services — works for free.
2. **Storage is a solved problem.** ClickHouse is the backend. We don't reinvent it; we wrap it well (via .NET Aspire's ClickHouse integration) and buffer inserts properly.
3. **The dashboard is the product.** Frontend gets first-class attention. It should look and feel unlike Seq or a Grafana panel.
4. **Least overhead to try.** `docker compose up` for v1. A `dotnet tool` CLI later. If trying Flare takes more than two minutes, we've failed.
5. **.NET-first DX, not .NET-only.** The getting-started experience is tailored to .NET loggers. But because ingestion is pure OTLP, polyglot teams aren't locked out.
6. **Scope discipline.** Ship a tight, excellent v1. Everything speculative goes in "Later," not v1.

---

## Architecture (v1)

```
  ┌─────────────────────────────────────────────┐
  │  Your apps / services                        │
  │  Serilog · NLog · ZLogger · MS ILogger · any │
  │  OTLP source (Go, Node, Python, ...)         │
  └───────────────────────┬─────────────────────┘
                          │  OTLP  (gRPC :4317 / HTTP :4318)
                          ▼
  ┌─────────────────────────────────────────────┐
  │  Flare.Ingest  (ASP.NET Core)                │
  │  • OTLP receiver (logs)                       │
  │  • Normalize → internal log-event model       │
  │  • Buffer + batch (Redis Streams)             │
  └───────────────────────┬─────────────────────┘
                          │  batched inserts
                          ▼
  ┌─────────────────────────────────────────────┐
  │  ClickHouse   (columnar store)               │
  └───────────────────────┬─────────────────────┘
                          │  SQL
                          ▼
  ┌─────────────────────────────────────────────┐
  │  Flare.Api  (ASP.NET Core query API)         │
  │  • Search / filter / aggregate                │
  │  • Live-tail stream (WebSocket / SSE)         │
  └───────────────────────┬─────────────────────┘
                          │  HTTP / WS
                          ▼
  ┌─────────────────────────────────────────────┐
  │  Flare.Dashboard  (SPA)                      │
  │  • Dense virtualized log table                │
  │  • Live tail · saved queries · charts         │
  └─────────────────────────────────────────────┘
```

Orchestrated in development with **.NET Aspire**.

### Component breakdown

| Component | Tech | Responsibility |
|---|---|---|
| `Flare.Ingest` | ASP.NET Core | Terminate OTLP (gRPC + HTTP), map to internal model, buffer, batch-insert to ClickHouse |
| `Flare.Api` | ASP.NET Core | Query/search/aggregate over ClickHouse; live-tail streaming endpoint |
| `Flare.Dashboard` | SvelteKit (Svelte 5, runes) + Tailwind + shadcn-svelte | The UI — the thing people come for |
| `Flare.AppHost` | .NET Aspire | Local orchestration of all of the above + ClickHouse |
| ClickHouse | container | Log storage and query engine |

---

## Ingestion: how each logger connects

All four terminate at the **same OTLP endpoint**. No Flare-specific client library is required for v1 — we lean on existing, maintained packages and provide copy-paste config.

| Logger | How it reaches Flare | Package |
|---|---|---|
| **Microsoft.Extensions.Logging** | Native | `OpenTelemetry.Exporter.OpenTelemetryProtocol` via `AddOpenTelemetry().AddOtlpExporter()` |
| **ZLogger** | Built directly on `ILogger` — flows through the MEL/OTel pipeline with zero bridge overhead | (same as above) |
| **Serilog** | Official OTLP sink | `Serilog.Sinks.OpenTelemetry` |
| **NLog** | OTLP target | `NLog.Targets.OpenTelemetryProtocol` |

The getting-started docs will show a short, self-contained snippet for **each** logger even though they converge — that per-library first impression is where the "least overhead" feeling actually lives.

> Note: some OTLP-for-logs packages track pre-release OpenTelemetry versions. Flare's ingest side depends only on the OTLP wire format, not on clients' package versions, so client-side churn doesn't break the server. Document known-good versions per logger.

---

## The dashboard (the part that matters)

This is where Flare wins or is forgettable. Targets for v1:

- **Dense, virtualized log table** — thousands of rows scroll smoothly; no pagination stutter.
- **Live tail** — real-time stream over WebSocket/SSE with pause/resume and backpressure handling. Feels like `tail -f`, looks like a product.
- **Fast structured filtering** — filter by level, service, time range, and arbitrary structured properties, with sub-second response.
- **Expandable events** — click a row to see full structured payload, scopes, exception details, trace/span IDs.
- **Saved queries** — name and re-run common filters.
- **Simple charts** — event volume over time, by level, by service. Enough to spot a spike, not a full charting suite.
- **A visual identity** — dark-first, the flare logo, a look that is immediately *not* Seq and *not* a Grafana panel.

Explicitly **out** of v1 dashboard scope: dashboards-as-code, arbitrary user-built panels, multi-tenant theming.

---

## Roadmap

### v1 — "Read your logs, beautifully" (MVP)
- [x] OTLP logs receiver (gRPC + HTTP) in `Flare.Ingest`
- [x] Internal log-event model + ClickHouse schema
- [x] Batched insert pipeline (buffer, flush by size/interval)
- [x] Query API: search, filter, time-range, aggregate
- [x] Live-tail streaming endpoint
- [x] Dashboard: log table, live tail, filtering, event detail, basic volume chart
- [x] `docker-compose.yml` — full stack up in one command
- [x] Getting-started docs with a snippet per logger (Serilog, NLog, ZLogger, MEL)

### Next — v1.1: Container distribution & Flare.Hosting.Aspire
Promoted out of "Later" (2026-08-07) — the `docker-compose.yml` v1 gate that blocked
this is now cleared. Sequenced: the CI item has to land and publish a real image
before the package item has anything to wrap.

- [x] **Docker Hub image publishing CI** — GitHub Actions workflow
      (`.github/workflows/docker-publish.yml`) that builds all three images
      (`xracer007/flare-ingest`, `xracer007/flare-api`,
      `xracer007/flare-dashboard`) from the existing Dockerfiles and pushes them to
      Docker Hub: `:edge` on every push to `main`, semver tags (+ auto `:latest`) on
      `v*.*.*` git tags. Single-arch (`linux/amd64`) for
      now — pre-alpha, no evidence yet anyone needs arm64; buildx is wired in from the
      start so adding `linux/arm64` later is a one-line change. **Done and verified
      2026-08-07** — merged via PR #8, `:edge` confirmed live and public on all three
      Docker Hub repos. Originally also build-only (no push) on PRs to validate the
      Dockerfiles pre-merge; dropped that trigger 2026-08-18 - it doubled CI time by
      rebuilding the same images a second time right after, once the merge's own push
      to `main` fired this workflow for real, for no benefit beyond a Dockerfile-break
      signal a few minutes earlier.
- [x] **`Flare.Hosting.Aspire` integration package** — publishable NuGet package
      (`src/Aspire.Hosting.Flare/`) exposing `builder.AddFlare("flare")` for any .NET
      developer already using .NET Aspire for their own app, wrapping the three images
      above (ClickHouse + Redis wired the same way `Flare.AppHost/Program.cs` does).
      Not `Flare.AppHost` itself — that stays Flare's private dev-inner-loop
      orchestrator, never published. `AddFlare()` builds a composite `FlareResource`
      (5 children attached via `WithParentRelationship`: ClickHouse + database, Redis,
      and the three `xracer007/flare-*` containers), with ClickHouse's init SQL
      embedded in the package and materialized to a temp dir for `WithBindMount`
      (resolves the open structural gap from the earlier design pass). NuGet metadata
      in place: icon (root `logo.png`), package `README.md`, MIT license, repo URL —
      not yet packed/pushed to nuget.org. **Built and e2e-verified 2026-08-08** —
      `aspire start` against a throwaway AppHost referencing the package, all 6 real
      resources reached `Running`/`Healthy` pulling the actual `xracer007/flare-*`
      images, and a real OTLP log POSTed to the ingest container's `:4318` round-tripped
      through Redis → ClickHouse and came back correctly from `flare-api`'s
      `/api/logs/search`. Two bugs only surfaced by that live run, now fixed: (1) the
      published `flare-ingest`/`flare-api` images hardcode connection-string names
      `"clickhousedb"`/`"redis"` (confirmed against their `Program.cs`) — the Aspire
      *resource* names stay `{name}`-prefixed for multi-instance safety, but each
      `WithReference` now passes an explicit `connectionName:` override so the injected
      env var matches what the images expect; (2) `PUBLIC_API_URL`/`ORIGIN` were
      resolving to Aspire's internal `*.dev.internal` container-network DNS (unreachable
      from a real browser) because plain `GetEndpoint("http")` defaults to container-
      network context for a container-to-container reference — fixed by passing
      `KnownNetworkIdentifiers.LocalhostNetwork` explicitly, since both env vars are read
      by the browser, not another container. **2026-08-08, before packing/publishing:**
      added `examples/` (an `ExampleApp.AppHost` + `ExampleApp.LogGenerator`, the latter
      using `Flare.ServiceDefaults` and a background worker emitting random structured
      logs) referencing the package via `ProjectReference` so it can actually be run and
      watched end-to-end, plus `docs/aspire-hosting.md` documenting `AddFlare()`'s
      current API and pre-publish status, cross-linked from the root `README.md`. Package
      itself is still not packed/pushed — that's the explicit next step, deferred until
      the user has tried the example.
- [x] **Expose Flare's ingest OTLP endpoint on `FlareResource`.** Today `AddFlare()`
      keeps the `ingest` container's gRPC endpoint as a private local variable
      (`Aspire.Hosting.Flare/FlareResourceBuilderExtensions.cs:106-115`) — never
      attached to the `flare` builder it returns. That's why every consuming resource
      currently needs a hand-written
      `.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", "http://localhost:4317")`
      (see `examples/ExampleApp.AppHost/Program.cs`), which only resolves correctly by
      coincidence of local dev topology and silently breaks once that consumer runs in
      its own container — under a `docker-compose` publish or a real Kubernetes/ACA
      deployment — where `localhost` no longer reaches Flare's ingest. Exposing the
      endpoint lets a consuming AppHost call `.WithReference(flare)` (or a small
      `WithOtlpEndpoint(flare)` convenience wrapper), which Aspire resolves correctly
      per execution context (loopback locally, container-network alias under compose,
      real Service DNS/ingress once published) instead of a hardcoded string — replacing
      the manual `WithEnvironment(...)` line, not supplementing it. Prerequisite for the
      `Flare.Aspire` client package below — `WithReference(flare)` needs something real
      on `FlareResource` to reference. Once built, update
      `examples/ExampleApp.AppHost/Program.cs` to consume it, replacing the current
      manual `WithEnvironment` line and its explanatory comment.

### v2 — `Flare.Aspire` (client-side package)
Mirrors the two-package shape of `Aspire.Hosting.Seq` / `Aspire.Seq`:
`Flare.Hosting.Aspire` (server/AppHost side, v1.1 above) pairs with a new `Flare.Aspire`
client package that a *consuming service project* references directly and calls from its
own `Program.cs` — the same convention every other Aspire client integration
(`Aspire.StackExchange.Redis`, `Aspire.Npgsql`, `Aspire.Seq`, ...) follows, just under our
own `Flare.*` prefix rather than `Aspire.*` — see the naming note below. Today the
consuming side needs zero Flare-specific code because ingest is pure OTLP — `Flare.Aspire`
earns its keep as a forward-compatible seam for the already-planned "Auth + multi-user /
roles" Later-roadmap item: once ingest needs an API key/token, the client package becomes
the natural place to attach it to the OTLP exporter, the same job `Aspire.Seq`'s client
package does today for Seq's own API key.

- [x] **`Flare.Aspire` package** (`src/Aspire.Flare/`) — `builder.AddFlareOtlpExporter("flare")`
      called from a consuming service project's own `Program.cs` (alongside or in place of
      `Flare.ServiceDefaults`'s existing OTel wiring), reading the connection info injected by
      `.WithReference(flare)` on the AppHost side and registering a second, **named** OTLP log
      exporter against it via the signal-specific `AddOpenTelemetry().WithLogging(l =>
      l.AddOtlpExporter(name, configure))` — additive alongside whatever exporter the app's own
      OpenTelemetry setup already registered, same mechanism `Aspire.Seq`'s `AddSeqEndpoint`
      uses. Logs only for now — Flare.Ingest doesn't receive traces/metrics yet. First e2e run
      (`ExampleApp.LogGenerator`) threw `NotSupportedException` at startup: the OTel SDK forbids
      mixing this signal-specific `AddOtlpExporter` family with the cross-cutting
      `UseOtlpExporter()`, and `Flare.ServiceDefaults.ConfigureOpenTelemetry()` was calling the
      latter (see below). A wrong first fix attempt assumed `UseOtlpExporter` had a named
      multi-destination overload per its XML docs - reflecting on the actually-installed
      `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.17.0 assembly showed that overload doesn't
      really exist in this version (its XML docs over-describe it); the real, verified fix was
      making `Flare.ServiceDefaults` signal-specific instead (below), letting the original
      `AddOtlpExporter(name, ...)` design stand. `FlareSettings` deliberately stays minimal
      (`Endpoint` + `Protocol` only, defaulting to gRPC) — no `ApiKey` placeholder yet; the
      package's existence *is* the forward-compat seam described above, and an unused property
      would be exactly the kind of speculative surface the scope-discipline principle warns
      against. No client-side health check yet either — the connection string is Flare.Ingest's
      OTLP/gRPC endpoint, not cleanly HTTP-health-checkable the way Seq's single HTTP endpoint
      is; that's a separate follow-up needing the HTTP endpoint threaded through too.
- [x] **`Flare.ServiceDefaults.ConfigureOpenTelemetry()` switched from `UseOtlpExporter()` to
      signal-specific `AddOtlpExporter()`** (`src/Flare.ServiceDefaults/Extensions.cs`) — required
      by the `Flare.Aspire` fix above: the two styles can't coexist in one `IServiceCollection`,
      and `Flare.Aspire` needs the signal-specific, named one to add Flare as a second
      destination. Behavior for existing consumers is unchanged — still reads the same
      `OTEL_EXPORTER_OTLP_*` env vars, just via `WithLogging/WithMetrics/WithTracing(x =>
      x.AddOtlpExporter())` instead of the single cross-cutting call.
- [x] `ExampleApp.LogGenerator` updated to consume `Flare.Aspire` directly, replacing its
      `Flare.ServiceDefaults`-only wiring — chosen over adding a second example project (the
      alternative this bullet originally described) since one example proving the full
      `Flare.Hosting.Aspire` + `Flare.Aspire` pairing is clearer than two partial ones.
      `ExampleApp.AppHost/Program.cs` now uses `.WithReference(flare)` instead of
      `WithOtlpEndpoint(flare)` (injects `ConnectionStrings__flare` rather than setting
      `OTEL_EXPORTER_OTLP_ENDPOINT` directly), and `ExampleApp.LogGenerator/Program.cs` calls
      `builder.AddFlareOtlpExporter("flare")` alongside its existing `AddServiceDefaults()`.
      **Built and e2e-verified 2026-08-08** — `dotnet build Flare.sln` clean, `aspire start`
      against `ExampleApp.AppHost`, `POST /generate-burst`, burst confirmed showing up in the
      Flare dashboard (user-verified). Merged via PR #12.
- [x] Getting-started docs updated to show the `Flare.Aspire` path for Aspire-orchestrated
      consumers, alongside the existing per-logger (Serilog/NLog/ZLogger/MEL) snippets for
      non-Aspire consumers. **Done 2026-08-09** — went further than just adding the
      `Flare.Aspire` snippet: split the docs by audience instead of leading with an
      unexplained `docker compose up`. `docs/getting-started.md` is now a short hub that
      forks on "already using .NET Aspire?"; `docs/aspire-hosting.md` covers that path
      (`AddFlare` + `Flare.Aspire`'s `AddFlareOtlpExporter`, `WithOtlpEndpoint` as the
      no-client-package fallback) and also fixed its stale "pre-alpha, not published"
      status - confirmed both `Flare.Hosting.Aspire` and `Flare.Aspire` are live on
      nuget.org at `0.1.1` via the flatcontainer API; `docs/standalone.md` (new) covers
      the non-Aspire path (the old `docker compose up` content, now with the
      clone/prereqs context it was missing) - Docker is documented as the only way to
      run standalone, no non-Docker install path is planned. README's Quickstart
      rewritten to present both paths instead of only the Docker one. Also corrected the
      same stale pre-alpha/not-published claims and a stale `WithOtlpEndpoint`-only
      snippet in `src/Aspire.Hosting.Flare/README.md` and `src/Aspire.Flare/README.md`
      (the nuget package READMEs, effective from the next release since nuget.org package
      pages are immutable per version).

**Package naming correction, 2026-08-08 (after the above shipped):** the first real publish
attempt (`aspire-hosting-flare-v0.1.0`, tagged after the whole v1.1/v2 body of work above)
exposed that `Aspire.` is a Microsoft-reserved ID prefix on nuget.org (confirmed by the
"Prefix Reserved" badge on official packages like `Aspire.Hosting.Redis`). Pushing under it
from a non-Microsoft account gets silently swallowed: nuget.org's push endpoint accepts the
upload into storage (permanently burning that exact id+version — NuGet never lets an
id+version be reused, even for a rejected upload) but never lists, indexes, or emails about
it — no error surfaces anywhere, including in the "successful" CI run's own log, unless you
read past its green checkmark to the `409 Conflict — already exists` line. Confirmed via the
nuget.org API directly: `v3-flatcontainer`/registration/package-page all 404, search 0 hits,
and the account's own Published/Unlisted package lists show nothing — despite a "success"
workflow run. Both `aspire-hosting-flare-v0.1.0` and `aspire-flare-v0.1.0` are unrecoverable
under those exact package IDs.

**Fix:** renamed the *published `PackageId` only* — `Aspire.Hosting.Flare` → `Flare.Hosting.Aspire`,
`Aspire.Flare` → `Flare.Aspire`. Deliberately **not** a project rename: directory names
(`src/Aspire.Hosting.Flare/`, `src/Aspire.Flare/`), `.csproj` filenames, the `.sln` entries,
and every C# namespace (`Aspire.Hosting`, `Aspire.Hosting.ApplicationModel`,
`Microsoft.Extensions.Hosting`) are unchanged — those `Aspire.*` namespaces are the documented
Aspire "Create custom hosting/client integrations" discoverability convention and aren't
policed by nuget.org's prefix reservation (only package *IDs* are). Old tags
`aspire-hosting-flare-v0.1.0` / `aspire-flare-v0.1.0` are left in git history as a record of
the incident, and the tag *prefixes* (`aspire-hosting-flare-v*`/`aspire-flare-v*`) stay as-is
in `nuget-publish.yml` — only the `<Version>` each package bumps to changed, to `0.1.1`, so the
first real release tags (`aspire-hosting-flare-v0.1.1` / `aspire-flare-v0.1.1`) don't collide
with the already-existing `v0.1.0` tags from the abandoned attempt.

### v3 — Alerting (threshold/query-based → webhook, email, Slack)
Promoted out of "Later" (2026-08-09). ~~Scoped down slightly from the original bullet:
webhook + Slack notifications now, email explicitly deferred (see below) — Slack's
simplest integration is itself just an incoming-webhook URL, so it shares almost all its
code with a generic webhook sink, while email needs its own SMTP credential/config design
and a mail-sending dependency this project doesn't have yet.~~ **Decided (2026-08-09):**
shipped in three passes instead of one — webhook/Slack first (below), then Telegram as a
follow-up, then email as a second follow-up once its SMTP credential/config design was
actually worked out (see the three bullets below) — closing out the full original scope.

- [x] **ClickHouse storage** — `db/clickhouse/0003_alert_rules.sql` /
      `0004_alert_events.sql`: `alert_rules` (rule definitions) and `alert_events`
      (append-only fired-notification history, also the sole cooldown-tracking
      mechanism). First "config, not log data" table in the schema — CRUD is INSERT-only
      (`ReplacingMergeTree` + `FINAL` reads, tombstone deletes), since ClickHouse has no
      good in-place-update story and `ALTER TABLE ... UPDATE/DELETE` are async mutations,
      the wrong tool for "write, read back immediately" CRUD. Full rationale in both
      migrations' own comments and `db/clickhouse/README.md`'s "Design decisions".
- [x] **Rule CRUD + evaluation-dry-run API** (`Flare.Api`) — `/api/alerts/*`: create,
      list, get, update, delete, fired-alert history, and two test endpoints (dry-run a
      saved rule or an unsaved draft against current data, ignoring cooldown). An alert
      rule's condition reuses `Flare.Api.Model.LogFilter` verbatim — the same filter DSL
      `/api/logs/search`/`/api/logs/aggregate` already compile via `LogFilterSqlBuilder`
      — the concrete payoff of keeping alerting in-process in `Flare.Api` rather than a
      new microservice.
- [x] **`AlertEvaluationWorker`** — a `BackgroundService` (same poll-loop idiom as
      `Flare.Ingest`'s `ClickHouseFlushWorker`) that re-evaluates every enabled rule's
      condition as a count over its own rolling window on every poll tick (default 30s),
      checks cooldown against `alert_events`, and notifies + records history on breach.
      Periodic polling only, matching "threshold/query-based" exactly — no
      streaming/near-real-time evaluation attempted, since the repo's only two
      `BackgroundService`s (the flush worker and live-tail's broadcaster) both already
      establish poll-loop as the idiom and there's no shared pub/sub bus to hook into
      instead.
- [x] **Webhook/Slack notifier** — `WebhookAlertNotifier` POSTs one JSON payload shape
      that serves both a generic webhook consumer (flat structured fields) and Slack's
      incoming-webhook parser (a top-level `text` field it renders as the message) —
      Slack ignores unrecognized top-level keys, so no per-rule "payload style" toggle
      was needed. Sent via a named/typed `HttpClient` that inherits
      `Flare.ServiceDefaults`' resilience handler (retries/circuit-breaking) for free.
- [x] **Dashboard: Alerts page** (`src/dashboard`) — the app's first route beyond Logs,
      and its first shared app-shell nav (`AppNav.svelte`, mounted in `+layout.svelte`;
      `LogsToolbar`'s own inline logo removed as now-redundant). Rule list (`Table`/`Card`
      shadcn components' first real use), create/edit form (`Dialog`'s first real use —
      a bounded form fits it better than `Sheet`'s established detail-viewer role from
      `EventDetailSheet`) with a live "test against current data" dry-run before saving,
      and a fired-alert history view (`Sheet`, matching `EventDetailSheet`'s precedent).
      Reuses `PopoverMultiSelect` and the severity-bucket utilities from the Logs
      Explorer for the condition builder; added shadcn `select`/`switch` components
      (composed from the already-installed `bits-ui`, no new npm dependency) for the
      threshold comparator and enabled toggle.
- [x] **Verified end-to-end, 2026-08-09** — real `docker compose`-built `api` image
      against real ClickHouse + Redis containers (existing dev volumes reused; the two
      new migrations applied manually since the ClickHouse image's init-script mount only
      auto-runs against an empty data directory): rule CRUD round-tripped via `curl`;
      inserted matching log rows directly into `logs` to simulate breaches; confirmed the
      dry-run test endpoints, the real `AlertEvaluationWorker` firing a real webhook with
      the exact designed payload (`Transfer-Encoding: chunked` — the resilience handler
      disables upfront content-length computation, a real quirk worth knowing, not a
      bug), cooldown correctly suppressing an in-window re-fire and firing again once
      elapsed, the rolling window correctly aging matching rows out and stopping further
      fires, and delete correctly stopping evaluation (one benign straggler fire was
      observed landing in the same poll tick as an in-flight delete — expected
      poll-based-system semantics, not a defect). Separately drove the dashboard through
      a real Chromium browser (Playwright): created a rule via the form, watched its live
      dry-run flip from "would not fire" to "would fire" as a matching log landed, saved
      it, watched the real worker fire it and the history sheet show the resulting
      "Sent (200)" entry, then edited and deleted it — all through the actual UI, not
      mocked. `dotnet test` clean (92 tests, including new `AlertThresholdTests` for the
      one pure piece of alerting logic); `npm run build` clean.
- [x] **Follow-up: Telegram notifier** (PR #23) — `TelegramAlertNotifier`, a second
      channel alongside webhook/Slack, mutually exclusive per rule (a rule picks exactly
      one channel; `AlertRuleRequest.ValidateChannel` enforces it). Needed its own
      request shape (`chat_id`/`text`/`parse_mode` to Telegram's `sendMessage`, not a
      bare webhook URL) since a bot token + chat id can't piggyback on the
      webhook/Slack payload trick the way Slack could. Notably, Telegram returns HTTP 200
      with `{"ok":false}` for most delivery failures rather than a non-2xx status, so
      success is derived from the parsed response body, not the HTTP status alone —
      otherwise failures would be misrecorded as sent. `CompositeAlertNotifier`
      introduced here as the actual `IAlertNotifier` registered for DI, picking the
      right concrete notifier per rule. `db/clickhouse/0005_alert_rules_telegram.sql`
      adds `TelegramBotToken`/`TelegramChatId`. Verified end-to-end against a real
      `docker compose` stack (channel-validation 400s, round-tripped a Telegram-only
      rule, existing webhook rules unaffected) and through the dashboard's real form
      dialog via a headless browser.
- [x] **Follow-up: Email/SMTP notifier** (closes the item originally deferred above) —
      `EmailAlertNotifier`, a third channel, via **MailKit** (new pinned dependency).
      Resolved the "own credential/config design" gap by treating SMTP as one app-wide
      server (`EmailOptions`, bound from config/`Email__*` env vars — same
      `AlertingOptions`-style pattern, wired into `docker-compose.yml`/`.env.example`
      with no working default, since there's no sensible default mail server) rather
      than per-rule credentials — a rule just supplies a recipient address (`EmailTo`),
      the same role `WebhookUrl`/`TelegramBotToken`+`TelegramChatId` already play.
      `db/clickhouse/0006_alert_rules_email.sql` adds `EmailTo`. Verified end-to-end
      against a real `docker compose` stack.

### Later (only if v1 gets traction)
- [x] ~~`dotnet tool install -g flare` CLI that scaffolds + launches the stack~~
      **Shipped 2026-08-16** — `src/Flare.Cli` (nuget.org package id `Flare.Cli`, not
      `flare`; that id is already taken/unlisted on nuget.org, unrelated old package -
      installed *command* is still `flare`, same PackageId-vs-command-name trick as
      `Flare.Hosting.Aspire`) wraps the standalone `docker-compose.yml` stack as a
      standing, cross-project instance: `flare start/stop/status/open/update/logs/
      doctor/destroy`, installable and runnable from anywhere via
      `dotnet tool install --global Flare.Cli`, no repo checkout required. Deliberately
      has zero interaction with Aspire orchestration - `aspire start` already covers
      that path; this fills the gap it can't (one long-running instance shared across
      unrelated local projects). E2e-verified against a real Docker daemon: full
      lifecycle, data persistence across stop/start, `destroy` actually wiping volumes
      (refuses without `--yes`), image-digest diffing on `update`, and Ctrl+C on
      `logs -f` cleanly killing the child `docker compose` process tree instead of
      orphaning it (caught and fixed during verification). See docs/cli.md.
      **Also surfaced a pre-existing, unrelated bug during verification** - see the
      identity-migration race item below.
- [x] **`flare` CLI: dashboard-parity commands.** Discussed 2026-08-16 - things the
      dashboard already shows/does that are also genuinely nicer from a terminal, all
      against endpoints `Flare.Api` already exposes (no backend work needed, just CLI
      clients):
      - [x] ~~`flare tail`~~ **Shipped 2026-08-16.** Live tail via the existing
        `GET /api/logs/tail` WebSocket (same one the Logs Explorer's live-tail uses),
        streamed/filterable straight to the terminal (`flare tail --service api
        --level error`). Distinct from `flare logs`, which is raw Docker container
        stdout, not app-level structured log events. `--level` accepts
        trace/debug/info/warn/error/fatal, expanded to OTel `SeverityNumber` ranges
        (mirrors `src/dashboard/src/lib/logs/severity.ts`'s bucket boundaries, not a
        shared reference - the CLI never takes a compile-time dependency on
        Flare.Api/the dashboard, same as its Docker-image relationship). E2e-verified
        against a real live-tail session (real OTLP traffic via
        `ExampleApp.LogGenerator`, level filtering, Ctrl+C cleanly closing the
        WebSocket with no leftover process). **Bug caught and fixed during
        verification**: `Flare.Api/README.md`'s live-tail example shows
        `{"type":"event",...}`, but the server actually emits `{"type":"Event",...}` -
        `LogTailJsonContext`'s camelCase `PropertyNamingPolicy` only rewrites property
        *names*, not this enum-typed `type` discriminator's *value*, which
        `UseStringEnumConverter` serializes as the raw PascalCase C# member name. A
        hand-rolled client built against the doc's literal example silently received
        zero events until this was caught (via a Python `websockets` probe reproducing
        the raw wire traffic) and fixed with case-insensitive parsing. The doc itself
        is still inaccurate - worth a one-line fix there too, not done as part of this
        item.
      - [x] ~~`flare search`~~ **Shipped 2026-08-22.** One-shot query against
        `POST /api/logs/search` (`flare search --service api --level error --since
        15m`), prints matching rows and exits. New file `Commands/SearchCommand.cs`,
        modeled directly on `TracesCommand.cs` (same `LogFilterWire`-style DTOs, same
        `TracesCommand.TryParseSince` reuse). `Commands/LogEventDtoWire`/
        `LogFilterWire`/`LogSearchRequestWire`/`LogSearchResponseWire` live here,
        `internal` so `flare export` reuses them rather than duplicating.
      - [x] ~~`flare alerts list` / `flare alerts test <id>`~~ **Shipped 2026-08-22.**
        Wraps `GET /api/alerts` + the dry-run test-fire endpoint
        (`POST /api/alerts/{id}/test`, ignores cooldown, writes nothing) - verify a
        Slack/webhook/email channel actually fires without waiting for a real
        threshold breach. New file `Commands/AlertsCommand.cs`. First command
        **branch** in this CLI (Spectre.Console.Cli `config.AddBranch("alerts", ...)`)
        - the only way to express a two-word `alerts list`/`alerts test <ID>` verb
        pair. E2e-verified against a real rule (created directly via
        `POST /api/alerts`): `list` renders name/enabled/threshold/window/channel/id
        correctly, `test <id>` called twice in a row both returned a normal result
        with no cooldown-suppression difference (confirming the dry-run really
        ignores cooldown as documented), and `test <random-guid>` hit the explicit
        404 path.
      - [x] ~~`flare export`~~ **Shipped 2026-08-22.** Dumps a time range to
        NDJSON (default)/CSV via `/api/logs/search` cursor pagination (1000/page,
        matching `export.ts`'s `EXPORT_PAGE_SIZE`) - a support-bundle-for-a-bug-report
        command. New file `Commands/ExportCommand.cs`. Streams each page straight to
        stdout or `-o <path>` as it arrives rather than buffering the whole result
        first (a CLI export isn't bound by a browser tab's lifetime the way the
        dashboard's own export dialog's hard `EXPORT_ROW_CAP` is - `--limit`,
        default 100000, is a safety cap instead). Field set matches that dialog's
        CSV/XLSX export for parity. Wired to `InterruptSignal` the same way `flare
        tail` is, so Ctrl+C mid-export flushes/closes cleanly instead of leaving a
        truncated file. **Bug caught and fixed during verification**: the file-output
        path (`-o`) used `Encoding.UTF8`, whose default preamble writes a UTF-8 BOM at
        the start of the file - corrupted the CSV header's first cell to
        `"﻿EventId"` for any reader that doesn't know to strip it (confirmed live
        via Python's `csv` module without `utf-8-sig`), and would have been even less
        forgiving for NDJSON parsers expecting `{` as the first byte. Fixed with an
        explicit `new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)`.
        E2e-verified against real data: NDJSON validated as real JSON per line, CSV
        validated as real RFC4180 (correct quoting on a message containing a comma,
        header/row column counts match, no BOM).
      - [x] ~~`flare apikey create`~~ **Shipped 2026-08-22.** Ingest API-key creation
        via `POST /api/ingest-keys` - scripted/CI OTLP setup without clicking through
        the dashboard's Settings page. New file `Commands/ApiKeyCommand.cs`, also a
        branch (`"apikey"` → `"create"`) for symmetry with `alerts` and to leave room
        for `apikey list`/`apikey revoke` later (endpoints already exist, not built
        here). Raw key is printed once with an explicit "won't be shown again"
        warning. E2e-verified: created a real key, confirmed the printed id/name/
        createdAt matches what `GET /api/ingest-keys` reports server-side (real
        persistence, not just a client-side echo). Didn't verify the key actually
        authenticates an OTLP call end-to-end - `IngestAuthOptions.IngestKeyRequired`
        is off by default on this stack (matching Flare's opt-in-auth philosophy
        elsewhere) and toggling it is a backend-config concern orthogonal to this CLI
        feature, not exercised here.
      All four are thin HTTP clients against endpoints `Flare.Api` already exposed -
      no backend changes. `docs/cli.md`'s Command reference table and
      `Flare.Cli.csproj`'s `<Version>` (0.1.2 → 0.1.3) updated to match. No auth header
      sent by any of the four, same pre-existing gap every HTTP-based command already
      has (Flare's auth is opt-in; these 401 today if a user has it enabled, exactly
      like `tail`/`traces`/`metrics` already do) - inherited, not solved here.
      Deliberately not `flare traces`/`flare metrics` tail equivalents - logs are the
      core product and the CLI's whole pitch is "least overhead," better to ship one
      thing (`tail`) really well than three thin ones.
- [x] **Dashboard terminal: port the same 4 commands.** Follow-up (2026-08-22), same
      day - the dashboard's own browser-based terminal (`src/dashboard/src/lib/
      terminal/`, opened via `TerminalModal.svelte`) already hand-ports `flare.cli`'s
      commands to TypeScript (`tail`/`traces`/`trace`/`metrics`/`metric`/`ingestion`,
      no code-sharing with the CLI - see that module's own header comment), so
      `search`/`export`/`alerts list`+`alerts test <id>`/`apikey create` needed the
      same port to keep the terminal's `help` matching `flare --help` for everything
      that isn't inherently host-bound (`registry.ts`'s own stated criterion for what
      belongs in `unavailable.ts` vs. a real command - none of these four touch the
      host, so none belong there). New files: `commands/search.ts` (ported from
      `SearchCommand.cs`, exports its filter-parsing helpers so `export.ts` reuses
      them rather than a third copy - same precedent `metrics.ts`'s `parseSince`
      already set for `metric.ts`/`ingestion.ts`), `commands/export.ts` (reuses the
      Logs Explorer's own existing `$lib/logs/export.ts` pipeline -
      `fetchAllForExport`/`eventsToBlob`/`downloadBlob` - wholesale rather than any
      new fetch/pagination code; deliberately diverges from `ExportCommand.cs`'s
      shape, not just its flags, since a browser can't stream to stdout/an arbitrary
      path: `--format` offers the dashboard's existing csv/xlsx/json/xml instead of
      the CLI's ndjson/csv, always triggers a real download instead of `-o`, and
      inherits `fetchAllForExport`'s existing 25,000-row cap instead of a `--limit`
      flag), `commands/alerts.ts` (first sub-dispatch command in this terminal -
      `args[0]` branches on `list`/`test <id>`, wraps `$lib/alerts-api.ts`'s existing
      `listAlertRules`/`testAlertRule` unmodified), and `commands/apikey.ts` +
      new `src/dashboard/src/lib/ingest-keys-api.ts` (no ingest-key TS client existed
      anywhere in the dashboard before this - the only prior trace was a hardcoded
      curl example in `data-sources/catalog.ts`'s "Add Data Source" guide; only
      `create` wrapped, matching `ApiKeyCreateCommand.cs`'s own deliberately-scoped
      surface). `registry.ts` updated to register all four (`search`/`export` after
      `tail`, `alerts`/`apikey` after `ingestion`). `npm run check` clean (0 errors,
      0 warnings, 4950 files). E2e-verified via a real Playwright-driven Chromium
      session against the live dashboard's terminal modal: `help` lists all four in
      the right place; `search --level error --since 720h -n 5` rendered real
      error-level rows; `alerts list`/`alerts test <id>` (twice, no cooldown
      difference) against a real rule created via `curl`, plus `alerts test
      <random-guid>` surfacing the 404 clearly; `apikey create` printed a real raw
      key; `export --format json` and `--format csv` both triggered real browser
      downloads (235 rows each, matching the CLI's own export of the same data),
      validated as real JSON/CSV. Every usage-error path also exercised live
      (`alerts`/`apikey` with no subcommand, `apikey create` with no name, `search`
      with an unrecognized flag, `export --format bogus`) - all surfaced the expected
      message. Test rule/key deleted and stack torn down after (volumes kept), same
      as the CLI track above.
- [x] **Logs Explorer: CSV/XLSX/JSON/XML export + "Share" link.** Client-side-only
      companions to the CLI's `flare export` idea above and to v7's saved-views/
      `?view=<id>` mechanism: a toolbar "Export" dialog that asks the user to pick both
      rows (exactly what's currently loaded in the table vs. the full filtered result
      set, paginated via the existing `/api/logs/search`, bounded by a client-side row
      cap - no new backend endpoint) and format - hand-rolled CSV/JSON/XML, or a real
      .xlsx workbook via SheetJS (the one new dependency, pinned to `cdn.sheetjs.com`'s
      patched build rather than the stale/CVE'd npm-registry `xlsx` package). JSON keeps
      `logAttributes` as a real nested object; XML nests it as `<LogAttributes><Attribute
      key="...">`; CSV/XLSX (flat rows) double-stringify it into one
      `LogAttributesJson` column - same underlying field set (`export.ts`'s `HEADER`/
      `eventToRow`/`eventToJsonObject`/`eventToXmlElement`), one writer per format,
      dispatched through a single `eventsToBlob(events, format)`. A first cut always
      exported the full filtered set silently, which read as "wrong" to anyone
      expecting an export of what's on screen - asking explicitly avoids guessing.
      Also a "Share" button that auto-creates a saved view from the current filter and
      copies its `?view=<id>` link, reusing v7's persistence/hydration wholesale
      rather than encoding filter state into the URL directly. Auth-enabled
      deployments mean a shared link only works for a recipient who's already an
      authenticated Flare user - not a public/anonymous
      share token; known limitation, not a gap to close here.
- [ ] Retention policies + cold storage to S3-compatible object store (**RustFS**)
- [x] **Multi-node scaling.** Distinct from the RustFS item above (retention/cold
      storage vs. horizontal availability/throughput - conflating the two in an
      earlier discussion was a real imprecision worth not repeating). Designed and
      implemented 2026-08-22, live-verified against a real running cluster (not just
      unit-tested) - see [`docs/clustering.md`](docs/clustering.md) for the full
      writeup, including several real config bugs (Keeper's default loopback-only
      bind, its config.d/ not auto-merging, Distributed cross-node auth, a
      per-shard-fragmented `schema_migrations` path) found and fixed by actually
      standing the cluster up, not assumed away.
      - **ClickHouse**: `db/clickhouse-cluster/*.sql` is an opt-in, 1:1 variant of
        every `db/clickhouse/*.sql` migration using `ReplicatedMergeTree`/
        `Distributed` tables (2 shards x 2 replicas) + a 3-node ClickHouse Keeper
        quorum, delivered as a standalone `docker-compose.cluster.yml` alongside the
        unchanged single-node `docker-compose.yml`. `Distributed` tables keep the
        plain table names (`logs`, `spans`, etc.), so zero code changes were needed
        in `Flare.Ingest`'s writers or `Flare.Api`'s query builders.
      - **Redis Streams consumer name**: no longer hardcoded - `LogEventPipelineOptions.ConsumerName`
        (and its spans/metrics siblings) now default to a machine/process-derived
        name (`ConsumerIdentity`), so multiple `Flare.Ingest` replicas safely share
        one consumer group. `docker-compose.cluster.yml` runs two `ingest` replicas
        against the cluster as a live demonstration of this.
      - **`Flare.Api` statelessness**: `AlertEvaluationWorker` now gates each tick
        behind a Redis-backed lock so N replicas don't duplicate alert evaluation/
        notifications, and the ASP.NET Core Data Protection key ring is now
        Redis-backed (`PersistKeysToStackExchangeRedis`) so the Entra/OIDC external
        sign-in cookie survives a mid-handshake request landing on a different
        replica. `LogTailBroadcaster` needed no change - verified each replica
        already reads the shared Redis Stream independently via plain `XREAD` and
        fans out only to its own locally-connected WebSocket clients, which is
        already correct under multiple replicas. `HostStatsPoller`/
        `DockerContainerPoller` are explicitly out of scope here - they're
        inherently single-host/single-Docker-daemon concepts, not a gap this item
        introduces.
      - **Deferred, not started**: SQLite `Identity__DbPath` → Postgres. Only
        required if actually running >1 `Flare.Api` replica; `docs/auth.md` already
        documents the trade-off and names the migration as a contained, mechanical
        follow-up (same table set - `Users`/`Sessions`/`IngestApiKeys`/
        `AuthSettings`/`EntraSettings`/`LdapSettings`/`OidcSettings`/
        `ProxyAuthSettings`/`schema_migrations` - different backing store, not a
        rewrite).
      - **Follow-up (2026-08-23), live-verified against a real 4-node cluster**:
        `IndexingQueryService`'s `system.*` introspection is no longer
        single-node-scoped under cluster mode (branches each query on
        `ClickHouse:ClusterMode` to go through
        `cluster()`/`clusterAllReplicas('flare_cluster', ...)` instead - confirmed
        all five queries aggregate correctly, e.g. row/growth counts sum across
        shards without doubling); `spans` now shards on `cityHash64(TraceId)`
        instead of `rand()` (confirmed 30 inserted traces landed with zero
        cross-shard overlap); and `SpanQueryService.GetTraceAsync` now sets
        `optimize_skip_unused_shards` (cluster mode only, best-effort not forced -
        confirmed via `system.query_log` that it actually prunes the shard that
        doesn't hold a trace, in both directions, while still returning correct
        results). See `docs/clustering.md`'s "Operational notes" and "Design
        decision" sections, including the one real caveat: this assumes no data was
        inserted under the old `rand()` key before the sharding change.
      - **Client-side load balancing (2026-08-23, fixed):** `clickhouse-lb`, an
        `nginx:alpine` reverse proxy, now round-robins ClickHouse's HTTP interface
        across all 4 nodes with passive failover; `ConnectionStrings__clickhousedb`
        for `ingest-1`/`ingest-2`/`api` points at it instead of a hardcoded
        `clickhouse-1`. Live-verified: killed `clickhouse-1` mid-traffic and saw
        zero failed requests, rotation across the other 3 nodes. See
        `docs/clustering.md`'s "ClickHouse load balancing" section.
      - **Follow-up (2026-08-23), fixed:** Drain clustering state now shares across
        `Flare.Ingest` replicas (the gap left open above - see `DrainPatternMatcher`'s
        remarks). Cluster storage moved behind a new `IPatternClusterStore` seam
        (`Flare.Ingest/Patterns/`): `InMemoryPatternClusterStore` (default) preserves the
        original per-process tree unchanged; `RedisPatternClusterStore` (opt-in via
        `LogPattern:SharedStore`, set on `ingest-1`/`ingest-2` in
        `docker-compose.cluster.yml`) shares cluster state across replicas through a
        StackExchange.Redis conditional transaction (compare-and-swap, no Lua), with
        `DrainPatternMatcher.MatchBatchAsync` grouping a flush batch by
        `(tokenCount, firstToken)` bucket first so this is one Redis round trip per
        distinct template in the batch, not per log line. Eviction is TTL-based
        (`LogPattern:SharedTemplateTtl`) rather than the in-memory store's exact
        `MaxTemplates` cap. `dotnet test` green (added convergence/fragmentation-contrast
        tests proving two matcher instances sharing a store now agree on `PatternId`,
        plus key-naming tests). See `docs/clustering.md`'s updated "Drain log-pattern
        clustering now shares state across `Flare.Ingest` replicas" section.
      - **Follow-up (2026-08-23), shipped: cluster topology/health surfaced in the
        dashboard.** Prompted by comparing against Seq's own Clustering page (an
        Enterprise-only, paid-tier feature there) - not gated here, and every
        limitation above was already closed out, so nothing blocked a dashboard view
        of it. Lands on the **Indexing page** (`/indexing`), not Resources
        (`/resources`) - `IndexingQueryService` is the piece already made
        cluster-wide aware (see the earlier "Follow-up (2026-08-23)" bullet above),
        while Resources' own `HostStatsPoller`/`DockerContainerPoller` are explicitly
        single-host/single-Docker-daemon concepts - the wrong home for cluster-wide
        state. New `GET /api/indexing/cluster` (`ClusterQueryService`) queries
        `system.clusters` for shard/replica topology and per-node `errors_count`,
        short-circuiting to `{ clusterModeEnabled: false, nodes: [] }` with no
        ClickHouse round trip at all on a default single-node deployment; also
        reports `LogPattern:SharedStore` (mirrored onto `api`'s own config, display
        only, since `api` never does Drain matching itself) so the earlier
        shared-pattern-store fix is visible on the dashboard instead of only
        discoverable by reading `docs/clustering.md`. New
        `IndexingClusterStatus.svelte` renders the topology grouped by shard with a
        healthy/error badge per node, and renders nothing at all when cluster mode is
        off. Deliberately out of scope, named not silently skipped: Keeper quorum
        health and replication queue/lag - see `docs/clustering.md`'s "Dashboard:
        cluster status on the Indexing page" section for the full writeup and why.
        No unit test added for `ClusterQueryService` itself - same "holds
        `IClickHouseClient`, not unit-tested against a fake" precedent
        `IndexingQueryService`/`AlertQueryService`/`LogQueryService` already follow;
        `dotnet build` clean across the full solution, `svelte-check` clean
        (0 errors/warnings) for the dashboard. Live-verified (2026-08-23) against a
        real `docker-compose.cluster.yml` stack, not just built/type-checked - and
        it caught a real bug: `estimated_recovery_time` is `UInt32` in
        `system.clusters`, not `UInt64` as first written, throwing
        `InvalidCastException` on every row and silently degrading to an empty node
        list until fixed (see `docs/clustering.md`'s updated "Dashboard: cluster
        status" section). After the fix: all 4 nodes (2 shards x 2 replicas, all
        `errorsCount: 0`) showed up correctly via `curl` and on the actual Indexing
        page's new "Cluster" panel.
- [x] **Benchmark: ingest throughput + query latency proof points.** Shipped
      2026-08-22 - a proof point for the "Flare inherits HA/scale from ClickHouse +
      Redis Streams for free" claim discussed elsewhere, measured rather than just
      asserted. Full methodology/results/reproduction steps:
      **[docs/benchmark.md](../docs/benchmark.md)**. Two new committed scripts
      (`scripts/seed-benchmark-logs.py`, `scripts/query-latency-benchmark.py`,
      stdlib-only Python, no new dependency) plus a small additive
      `ExampleApp.LogGenerator` change (`/generate-throughput`, a saturating
      logs-only load generator, isolated from the demo trickle's 5-span-per-log
      overhead). Headline findings: ingest sustains ~1,000-1,100 events/sec
      end-to-end, flat across producer concurrency (1/4/16) - traced to the .NET OTel
      SDK's own default bounded `BatchLogRecordProcessor` queue silently dropping
      records under contention, not conclusively isolated from `Flare.Ingest`'s own
      single-threaded pipeline (named follow-up, not done here); query latency
      across 6 schema-motivated patterns at 5M seeded rows ranged 28.7ms (TraceId
      exact match) to 337.1ms p50 (unscoped attribute filter) - and, a genuine
      surprise stated plainly rather than smoothed over, the schema's own named
      "worst case" (unfiltered all-services aggregate) was *not* the empirical worst
      case at this scale. This is the RustFS-retention item's deferred sibling from
      the same track - RustFS itself intentionally not started here.
- [x] ~~Fix identity-migration race between `ingest`/`api`.~~ **Shipped 2026-08-18** —
      discovered 2026-08-16 while e2e-verifying the `flare` CLI's `destroy` → fresh
      `start` cycle (see above): against a genuinely empty `identity-data` volume,
      `ingest` crashed with `Microsoft.Data.Sqlite.SqliteException: SQLite Error 1:
      'duplicate column name: ExternalId'`. Root cause: `ingest` and `api` are separate
      processes that both point at the *same* shared SQLite file (`Identity__DbPath`)
      and each independently runs `Flare.Identity.IdentityMigrationRunner` on startup
      with no lock between them - on a fresh database both see migration
      `0002_entra_id.sql` as unapplied and race to run it; the loser crashed adding a
      column that already existed. Fixed by wrapping `IdentityMigrationRunner.ApplyAsync`'s
      whole body in a `BEGIN IMMEDIATE`/`COMMIT` transaction (raw SQL, since
      `Microsoft.Data.Sqlite`'s `BeginTransaction()` has no IMMEDIATE mode) with a
      30s `busy_timeout` scoped to that connection - SQLite's own write lock now serializes
      the two processes, so the loser blocks on `BEGIN IMMEDIATE` until the winner
      commits, then re-reads `schema_migrations` and finds everything already applied.
      Required stripping the inner `BEGIN TRANSACTION`/`COMMIT`/`PRAGMA foreign_keys`
      wrapper out of the three table-rebuild migrations (`0005_ldap_id.sql`,
      `0008_oidc_id.sql`, `0010_proxyauth_id.sql`, each of which used to open its own
      transaction) - SQLite has no nested `BEGIN`, so that toggling now happens once in
      the runner itself, around the whole batch. Also added `restart: unless-stopped` to
      `ingest`/`api` in both `docker-compose.yml` and the CLI's
      `docker-compose.flare.yml` as complementary hardening (previously a crashed
      container with no restart policy just sat `Exited` until someone noticed).
      Verified against a real fresh Docker volume: `api` applied all 10 identity
      migrations, `ingest` applied zero (blocked, then found everything already
      committed) - no crash, no duplicate-column error. Also incidentally validated the
      new `restart:` policy for real: an unrelated pre-existing ClickHouse-readiness
      race (see new item below) crashed both `ingest` and `api` from within the process
      3x each during this same verification run, and `restart: unless-stopped`
      auto-recovered all 6 crashes with no manual intervention.
- [x] ~~`ClickHouseMigrationRunner` doesn't retry/wait on ClickHouse connection-refused
      at startup.~~ **Fixed 2026-08-19 (code+build-verified only, live e2e still
      pending - Docker Desktop wasn't running on this machine to redo the
      `docker compose up --build` repro).** Found 2026-08-18 while verifying the
      identity-migration race fix above (see that item): even with `ingest`/`api`'s
      `depends_on: clickhouse: condition: service_healthy`, a fresh
      `docker compose up --build` still saw both containers throw an unhandled
      `System.Net.Http.HttpRequestException: Connection refused (clickhouse:8123)` out
      of `ClickHouseMigrationRunner` on their first startup attempt, crashing the
      process - Compose's `service_healthy` gate isn't quite tight enough to guarantee
      ClickHouse is actually accepting connections by the time the dependent
      container's own migration code runs. Not a new blocker in practice - both
      containers already carried `restart: unless-stopped` (see above) and self-healed
      automatically (3 crash/restart cycles each, all recovered) - but the underlying
      gap was real. Fix: `ClickHouseMigrationRunner`'s first statement (the bootstrap
      `CREATE DATABASE IF NOT EXISTS clickhousedb`) is now wrapped in a
      connection-failure retry/backoff loop (1s/2s/4s/8s.../8s, 10 attempts, ~65s total)
      that only retries transport-level failures (`HttpRequestException`,
      `SocketException`, `IOException`, including wrapped in a driver exception's
      `InnerException`) - a real query/schema error still fails fast instead of
      retrying for a minute. No call-site changes needed (`Flare.Api`/`Flare.Ingest`
      `Program.cs` both still call `ApplyAsync(client, logger, cancellationToken)`
      as before) since the retry lives inside the method itself. `restart:
      unless-stopped` stays as the outer safety net either way.
- [x] ~~Auth + multi-user / roles~~ **Shipped 2026-08-10 (see v11 below)** — local
      username/password + RBAC (Admin/Member/Viewer) on one shared instance, not
      multi-tenant isolation, per this doc's own "self-hosted, single-instance" framing;
      identity lives in an embedded SQLite file rather than a fourth backing-store
      container, mirroring Seq's own footprint-conscious design.
- [x] ~~OTLP traces & metrics (become a real observability tool)~~ **Traces half done,
      2026-08-10 (see v4 below) — metrics remains a separate, later item** (materially
      different data model: 5 point types vs. one span shape; bundling it would have
      roughly doubled v4's scope for no shared payoff).
- [x] ~~OTLP metrics (split out of the item above, 2026-08-10)~~ **Shipped 2026-08-10
      (see v6 below)** — Gauge/Sum/Histogram point types (ExponentialHistogram/Summary
      deliberately deferred, same rationale as v4's omitted Span Links).
- [x] ~~Trace/log correlation view~~ **Shipped 2026-08-10 (see v5 below)** — correctly
      sized as a single, non-staged session once v4 landed, per the plan's own
      pre-session cost read.
- [x] ~~Saved dashboards / shareable views~~ **Shipped 2026-08-10 (see v7 below)** —
      scoped as saved-per-page filter state (Logs/Traces/Metrics), not a multi-panel
      dashboard builder, per this doc's own "dashboards-as-code, arbitrary user-built
      panels" non-goal.
- [x] ~~**Logs: correlate a log event to its enclosing span's duration.**~~ **Shipped
      2026-08-21 (see v19 below)** - discussed the same day, while scoping the Logs
      page's Value distribution chart (a per-event scatter/density plot of any numeric
      `LogAttributes` key, shipped the same session - not yet given its own
      version-section writeup below, a known doc gap same shape as v14/v15's).
      Deliberately *not* done as part of that chart, for the same reason v16's Patterns
      feature already scoped out logs↔spans duration (see that item's "no duration/p95
      in v1" note): `spans.DurationNano` is only known once a span *ends*, and most log
      lines are flushed to ClickHouse *during* their enclosing span, before that
      duration exists - so this isn't a "join vs. precompute, pick the cheaper one"
      choice the way `PatternId`/`spans.DurationNano` themselves were (both computable
      at their own natural write time, no missing dependency). Two heavier shapes were
      scoped out at the time, kept here for the record since v19 below only covers a
      narrower "one page at a time" version of this problem - either could still be
      needed for a genuinely cross-query feature later (e.g. duration/p95 in Patterns,
      or sorting/filtering Logs by duration):
      - **Query-time `logs ⋈ spans` join** on `(TraceId, SpanId)`. Real cost, not just
        theoretical: `spans` is ordered `(TraceId, StartTime, SpanId)` (leads with
        TraceId) but `logs` is ordered `(ServiceName, SeverityNumber, Timestamp, TraceId)`
        (TraceId is 4th) - the mismatch means no cheap sorted-merge, ClickHouse falls back
        to a hash join materializing the filtered `spans` side into memory, paid on every
        query. Only ever covers logs that carry a non-empty `TraceId`/`SpanId` to begin
        with. Would need a narrow time-window cap to stay safe.
      - **Async backfill/enrichment**, mirroring `spans.DurationNano`'s own "stored
        explicitly, not computed at query time" precedent but applied after the fact: once
        a span closes, patch its already-written log rows with the span's duration via a
        ClickHouse mutation. Avoids the per-query join cost entirely, at the price of
        eventual consistency (a log's duration column is briefly absent right after
        ingest) and a new piece of pipeline machinery to track "which spans just closed."
      Neither shape was used - v19 found a third one, precedented elsewhere in this same
      codebase, that a single log-event/single-page view doesn't need either cost for.
      The pragmatic alternative already shipped and still needs no schema change: an app
      that logs a `duration_ms`-style attribute on its own completion log line is already
      chartable today via the Value distribution chart, no join, no ordering gap - same
      reason `spans.DurationNano` itself has no ordering gap (both are known at their
      producer's own natural write time).
- [x] **Helm chart for Kubernetes.** Not hand-authored - Aspire's built-in Kubernetes
      hosting integration (`Aspire.Hosting.Kubernetes`, `builder.AddKubernetesEnvironment()`
      + `WithHelm(...)`) generates Helm/K8s manifests at `aspire publish` time, same shape
      of work as the Docker Compose publish support already shipped in
      `Aspire.Hosting.Flare` (PR #176, `FlareResourceBuilderExtensions.cs`,
      `docs/aspire-hosting.md`'s "Known limitations" section) - extend that package so a
      *consumer's* `aspire publish` targeting Kubernetes works, not a chart maintained in
      this repo. Scoped (2026-08-29):
      - **v1 targets existing/external clusters only** (`AddKubernetesEnvironment`, current
        `kubectl` context) - not Azure Kubernetes Service (AKS, `AddAzureKubernetesEnvironment`,
        which also provisions ACR/identity/Azure infra). AKS is a separate, later item if
        ever needed; deliberately not bundled in since it drags in Azure subscription/
        resource-group/auth concerns `AddFlare` has never needed before.
      - **Resources-page topology parity (`flare.resource`/`flare.role` labels,
        `enableResourceGraph`) is explicitly out of scope for v1**, not silently dropped.
        The Docker Compose fix added labels two ways - `WithContainerRuntimeArgs` for DCP,
        `PublishAsDockerComposeService` for compose - and Kubernetes would need a third,
        `PublishAsKubernetesService(...)` pod/deployment labels, to avoid the same silent
        breakage `WithFlareResourceLabels`'s doc comment already warns about for compose.
        But `enableResourceGraph`'s actual discovery mechanism bind-mounts
        `/var/run/docker.sock` via a docker-socket-proxy sidecar - that has no equivalent
        inside a K8s pod, so adding just the labels would only be half a fix. Document the
        Resources page as Compose/DCP-only until a real K8s-API-based discovery redesign is
        worth doing.
      - **Open verification item carried over from the Compose work**: the custom
        ClickHouse-init image (`WriteClickHouseInitDockerContext`, a generated `Dockerfile`
        build context, not a bind mount) built fine for local `docker compose build`/`up`.
        Kubernetes has no such local-build-and-run path - `aspire deploy` would need to
        build *and push* that image to a registry the cluster can pull from, which per
        Aspire's own Kubernetes hosting docs means a `AddContainerRegistry(...)` resource is
        required for any locally-built image (Flare's own pre-published `xracer007/flare-*`
        images need no registry, only this one generated image does). Not yet confirmed
        whether `aspire deploy`'s Kubernetes pipeline actually builds+pushes a
        `WithDockerfile` context automatically or whether this needs explicit wiring -
        verify against a real cluster before calling this shipped, same "only Docker Compose
        is verified end-to-end" caveat `docs/aspire-hosting.md` already carries for the
        Compose target.
      - **Started 2026-08-29 - real bug found and fixed, artifact generation verified.**
        Built a throwaway scratch AppHost (`AddKubernetesEnvironment` +
        `AddContainerRegistry`/`WithContainerRegistry`, `ProjectReference` to
        `Aspire.Hosting.Flare`, outside the repo tree) and ran `aspire publish` against it.
        First attempt crashed outright, not just missing labels: `WithFlareResourceLabels`
        called `PublishAsDockerComposeService` *unconditionally* on every Flare container,
        which turns out to register Aspire's own `validate-docker-compose` pipeline step
        regardless of target - so `AddFlare` could only ever be published to Docker Compose;
        publishing to Kubernetes (or presumably Azure/AWS) hard-failed with "Resource
        '...' is configured to publish as a Docker Compose service, but there are no
        'DockerComposeEnvironmentResource' resources." **Fixed**: that call is now gated on
        an actual `DockerComposeEnvironmentResource` existing in the model (bumped
        `Flare.Hosting.Aspire` 0.2.1 -> 0.2.2, a patch/behavior-fix bump). Re-ran `aspire
        publish` after the fix - full Helm chart generated successfully (`Chart.yaml`,
        `values.yaml`, `templates/` for all five sub-resources). Inspecting the generated
        manifests surfaced three more real findings, written up in
        `docs/aspire-hosting.md`'s new Kubernetes subsection: (1) the registry requirement is
        confirmed narrowly scoped to just the ClickHouse-init image, as suspected; (2)
        `WithDataVolume()`/`WithVolume()` render as non-persistent `emptyDir: {}` under
        Kubernetes unless the consumer explicitly wires `AddPersistentVolume` themselves -
        **silent data-loss-on-reschedule risk for ClickHouse/Redis/identity data**, arguably
        the most important caveat found, not anticipated when this item was first scoped;
        (3) `publicApiUrl`/`publicDashboardUrl`'s absence resolves to Kubernetes' in-cluster
        Service DNS (e.g. `http://flare-api-service:8080`) instead of something
        browser-reachable, same failure shape already known from Compose, just confirmed to
        apply here too. **Still open**: no live cluster/Helm available on the verifying
        machine, so only `aspire publish` artifact generation is verified - `aspire
        deploy`/`helm install` against a real cluster remains unverified, same "known
        limitations" honesty bar `docs/aspire-hosting.md` already holds Compose to.
        `ImagePullPolicy.Always` also didn't appear to translate into the generated
        manifests (`imagePullPolicy: IfNotPresent` throughout) - noted but not chased down,
        low-severity (only matters for the `"edge"` tag, which a real deployment wouldn't
        normally use).
      - **Same day, continued - closed the "no live cluster" gap via k3s, found and fixed
        a second, more severe real bug, then verified a real `aspire deploy` end-to-end.**
        User pointed at [aspire.dev/integrations/compute/k3s](https://aspire.dev/integrations/compute/k3s/)
        (`CommunityToolkit.Aspire.Hosting.K3s` runs a real k3s cluster inside a Docker
        container - exactly the missing piece). Installed `helm` via brew (host had
        `kubectl` already), stood up a real k3s cluster via plain `docker run
        rancher/k3s` (privileged, on its own Docker network) plus a local `registry:2`
        container wired into k3s's containerd as an insecure mirror for the ClickHouse-init
        image. First `aspire deploy` against it failed for real, not a fluke: ClickHouse's
        and Redis's `StatefulSet`s could never create a pod at all -
        `spec.volumes[0].name: Invalid value: "k8sverify.apphost-...-flare-clickhouse-data":
        must not contain dots`. Root cause: Aspire's Kubernetes publisher derives
        `WithDataVolume()`'s *default* volume name from the AppHost project's own name
        without DNS-1123 sanitization, and `.AppHost`-suffixed project names - the standard
        Aspire template convention, used by `examples/ExampleApp.AppHost` and this repo's
        own `Flare.AppHost` alike - produce a name containing a dot. **This meant Kubernetes
        support was completely broken for any consumer using the standard naming
        convention**, not an edge case. **Fixed**: pass an explicit dot-free name to both
        (`{name}-clickhouse-data`/`{name}-redis-data}`), the same pattern the identity
        volume already used (bumped `Flare.Hosting.Aspire` 0.2.2 -> 0.2.3). Re-ran `aspire
        deploy` after the fix - full pipeline succeeded, ClickHouse ran its init SQL,
        ingest/api connected to Redis and started listening, all pods (including both
        StatefulSets) reached `Running 1/1`, and `flare-api`'s `/health` returned `200`
        through a real Kubernetes `Service` (port-forwarded and curled). Cleaned up the k3s/
        registry containers afterward. This closes the "actual live-cluster deploy
        unverified" gap noted above - not just artifact generation anymore, a genuine
        `helm upgrade --install --wait` against a running cluster. Write-up in
        `docs/aspire-hosting.md`'s Kubernetes subsection and "Known limitations."
      - **v1 considered done** against its own scope (existing/external clusters,
        Resources-page parity deferred) - checkbox flipped. Remaining known caveats
        (persistent volumes not wired by default, `ImagePullPolicy.Always` gap, AKS/
        multi-deployment-environment untested) are documented limitations for later, not
        blockers to calling this shipped.
- [x] ~~**Ingestion page: pipeline health.** Scoped out of v8's MVP on purpose -
      throughput/rejected-payload stats (v8) answer "is data arriving"; this answers "is
      the buffered pipeline keeping up," which needs its own design pass.~~ **Promoted and
      shipped 2026-08-10 (see v10 below)** - the design pass landed the same day it was
      requested rather than waiting for a real incident, since the user asked for it
      directly.
- [x] **Logs page `VirtualList` hardening.** Backlog from a 2026-08-17 deep-dive into
      the Logs page's virtualizer (`src/dashboard/src/lib/components/virtual-list/
      VirtualList.svelte`), after three swapped-in library replacements
      (`@tanstack/svelte-virtual`, `@humanspeak/svelte-virtual-list`) each hit the same
      wall - none reconcile scroll position against an *externally*-owned `items` array
      being prepended to (live-tail), only against changes they drive themselves. Ended
      back on the hand-rolled component with a bounded-key-scan scroll-compensation
      effect (handles both live-tail prepend and `PAGINATION_CAP`/`LIVE_CAP` eviction
      from the front) plus `overflow-anchor: none` (the actual root cause of a
      "gap appears mid-scroll while live, only a reload fixes it" report - native scroll
      anchoring was fighting the manual compensation effect over the same `scrollTop`).
      A follow-up read of `@humanspeak/svelte-virtual-list`'s actual source (not just
      its README) surfaced concrete techniques worth porting, since fixed-row-height
      sidesteps everything in that library that exists only for *unknown*/measured row
      heights (its height-cache, block-sums, per-item ResizeObserver, grid detection,
      and orientation-switching are all irrelevant here - skip re-researching those):
      - [x] ~~Keyboard accessibility, currently entirely absent~~ **Shipped 2026-08-18** -
        `role="region"` + `aria-label` + `tabindex="0"` on the scrollable viewport, a
        keydown handler (arrows/PageUp/PageDown/Home/End, fixed-px line step -
        deliberately *not* derived from `itemHeight`, same reasoning native scroll uses)
        that checks "is this even a scroll key" before touching any layout property so
        an unrelated keypress never forces a stray reflow, and a *inward*-drawn focus
        ring (`outline-offset: -2px`, since the viewport clips outward outlines) keyed
        off the ARIA attributes rather than a class name so it survives a future
        `class` override. Svelte's a11y linter flags `role="region"` + tabindex/keydown
        as "non-interactive" by default; suppressed with `svelte-ignore` comments citing
        the ARIA APG scrollable-region pattern this actually follows.
      - [x] ~~`ResizeObserver` has no guard today against a bogus zero-height reading~~
        **Shipped 2026-08-18** - a transient 0 mid-animation/tab-switch/detach-reattach
        would have collapsed the visible range to nothing for a frame; now ignores
        non-finite/`<= 0`/unchanged readings and keeps the last known-good height.
      - [x] ~~Dev-mode-only safety nets directly relevant to the bug class this whole
        session was about~~ **Shipped 2026-08-18** - a duplicate-`getKey` assertion (a
        plain `Set`, not a reactive Svelte collection, per humanspeak's own comment that
        a reactive one caused a ~10s stall on a 10k-item list from capturing a stack
        trace per key) and a "same `scrollTop` written more than 10x in 1s" canary as a
        cheap feedback-loop detector - both funnel through one `writeScrollTop()`/
        duplicate-key `$effect` gated on a build-time-inlined `DEV` constant, so both are
        dead-code-eliminated from the production bundle (verified against the built
        client chunks) rather than merely no-op'd at runtime.
      - [x] ~~No validation on the `itemHeight` prop~~ **Shipped 2026-08-18** - a
        `safeItemHeight` derived validates once at the single point `totalHeight`/
        `visibleCount`/`startIndex`/`offsetY`/the scroll-compensation math all read from,
        falling back to 1px (keeps the math finite - a misconfigured row height now
        renders visibly squashed instead of invisibly NaN) with a dev-only
        `console.error` so the caller notices; the fallback itself applies
        unconditionally, only the warning is dev-gated.
      All five items from this backlog are now shipped - `VirtualList` hardening is
      done.
- [x] ~~**Metrics chart: remaining aggregation-mode options (Count for Sum; p75/p95/Max
      for Histogram).** 2026-08-19 - Tier 2 of the chart header's aggregation-mode
      picker (`MetricChart.svelte`'s `sumMode`/`histogramMode` Select next to the
      type). Tier 1 shipped the same day - Sum↔Rate and Percentiles↔Mean - because
      both are pure client-side reshapes of data the API already returns in full
      (`value`/bucket-width for rate; `sum`/`count` for mean - see
      `MetricSeriesPoint`), no query change needed. These four are not:
      - **Sum → Count.** `MetricSeriesQueryBuilder`'s Sum branch only ever selects
        `max(Value) - min(Value)` per bucket - there's no `count()` of raw samples.
        Needs a new query branch (or an added `count(*) AS Count` column alongside the
        existing one) plus a new response field wired through `MetricModels.cs` →
        `metrics-api.ts`.
      - **Histogram → p75, p95.** Cheap in principle - `HistogramQuantileEstimator`
        already does the interpolation `MetricQueryService` calls it with for
        p50/p90/p99, just call it at two more quantiles - but `MetricSeriesPoint` has
        fixed `P50`/`P90`/`P99` fields, not a general list, so this also wants a small
        API shape change (either add two more fixed fields, the path of least
        resistance, or move to a `percentiles: Record<string, double>` map if this
        keeps growing).
      - **Histogram → Max.** Not derivable from anything currently stored
        (`BucketCounts`/`ExplicitBounds`/`Sum`/`Count` - no true max). Two options:
        capture the OTLP `HistogramDataPoint.Max` field end-to-end (`OtlpMetricsMapper`
        → ClickHouse schema → query) *if* the producer actually sets it (optional
        per the OTel proto, plenty of SDKs omit it), or approximate as "upper bound of
        the highest non-empty bucket" using `ExplicitBounds` already in hand - honest
        about being an approximation, but zero schema change. Whichever is picked,
        should be a deliberate choice, not a silent one - same "don't silently
        half-solve it" precedent `MetricSeriesQueryBuilder`'s own Sum-delta remark
        sets.~~ **Shipped 2026-08-20 (code+unit-test+live e2e-verified).** All three
        went with the "path of least resistance"/"zero schema change" options named
        above, made deliberately rather than silently:
      - **Sum → Count**: `count()` (bare, matching the codebase's existing convention)
        added alongside `max(Value) - min(Value)` in the Sum branch; the result reuses
        `MetricSeriesPoint.Count` (already unused for Sum) rather than a second field,
        same "one field, type-dependent meaning" pattern `Value` already has.
      - **Histogram → p75, p95**: added as fixed `P75`/`P95` fields (the named
        least-resistance option - `HistogramQuantileEstimator.Estimate` already took an
        arbitrary quantile, so this was two more call sites, not a signature change).
      - **Histogram → Max**: shipped as the bucket-bound approximation (new
        `HistogramQuantileEstimator.EstimateMax`, exposed as `MetricSeriesPoint.
        MaxApprox`), not true OTLP `HistogramDataPoint.Max` end-to-end - decided
        deliberately over true-capture because that OTLP field is optional and often
        left unset by .NET's own OTel histogram instrumentation (Flare's primary
        audience), so the "exact" version risked frequently rendering null, while the
        approximation works retroactively on all existing data with zero schema/ingest
        changes. Labeled "Max (approx.)" everywhere it's user-visible (Select item,
        trigger, legend/tooltip) so it's never mistaken for a real max.
      - All five new `MetricChart.svelte` Select options (Count; p75/p95/Max (approx.))
        render as their own single line, sharing one reused color slot
        (`SINGLE_LINE_COLOR`, ex-`MEAN_COLOR`) with Mean since they're mutually
        exclusive. Comparison mode (period-over-period) was extended to the new **Max**
        option too - a "max of per-bucket `maxApprox` across the period" is a valid
        aggregation, unlike averaging percentiles - but deliberately *not* to p75/p95,
        which stay alongside Percentiles in the unavailable bucket for that same
        "can't validly average percentiles across a period" reason (and the frontend
        never receives the raw bucket data a true whole-period percentile would need
        anyway). Verified live end-to-end: posted real Sum/Histogram OTLP metrics
        through `docker compose`'s ingest container, confirmed the new
        `count`/`p75`/`p95`/`maxApprox` fields in the raw `/api/metrics/query`
        response, and drove the dashboard (Playwright) through all five new Select
        options, confirming correct rendering and the "Max (approx.): 500 ms"
        tooltip wording.
- [x] ~~**Metrics chart: configurable "Group by" attribute.** 2026-08-19 - lets a user pick
      which attribute key (`error.type`, `service.name`, `deployment.environment`,
      `host.name`, etc.) defines a series, collapsing everything else. Motivated by a
      metric like `dotnet.exceptions` carrying many `error.type` values on the same
      service: as of 2026-08-19, `MetricChart.svelte`'s `compactSeriesLabel()` hides
      attributes that are already constant across the *visible* series (so the legend
      reads `InvalidOperationException`/`TimeoutException` instead of repeating
      `log-generator · error.type=...` on every line, full dimension set still one
      hover away) - a real mitigation, but a labeling one, not grouping. It doesn't
      reduce series count, so a metric with 15+ `error.type` values still hits
      `MAX_SERIES`'s 5-series cap and silently drops the rest behind "+N more series
      not shown". Actual grouping needs backend work, not just a frontend picker:
      - **A different aggregation, not a relabel.** `MetricSeriesQueryBuilder` currently
        groups one series per distinct `(ServiceName, DataPointAttributes)` pair (see its
        own remarks on why `ServiceName` is a separate `GROUP BY` column). Grouping by a
        chosen attribute means collapsing every series that shares that one key's value
        regardless of what else differs - a new `GROUP BY` shape, and the existing
        per-type value expressions (`max(Value) - min(Value)` for Sum, `sum(Count)`/
        `sum(Sum)`/`sumForEach(BucketCounts)` for Histogram) need to keep meaning the
        same thing over the wider, coarser groups.
      - **Needs attribute-key discovery.** The picker can't hardcode `error.type`/
        `service.name`/etc. as options - it needs to know which keys actually exist on
        the currently-selected metric, which means a new query (or extending
        `MetricNamesQueryBuilder`'s discovery pass) to enumerate `DataPointAttributes`
        keys, not just names.
      - **Interacts with the `MAX_SERIES` cap.** Grouping by a low-cardinality key
        (`deployment.environment`) makes the 5-series cap almost never bind; grouping by
        a high-cardinality one (`host.name`) could still exceed it - the cap and its
        "narrow the filter" hint stay relevant either way, just less often triggered.~~
      **Shipped 2026-08-20 (code+unit-test+live e2e-verified).** `MetricQueryRequest`
      gained `GroupByAttributeKey` (null/empty = ungrouped, same convention
      `MetricFilter.Services` uses). `MetricSeriesQueryBuilder` branches `SeriesKey`/
      `SeriesAttributes` on it - grouped mode uses `DataPointAttributes[key]` as the key
      and a synthetic `map(key, any(DataPointAttributes[key]))` as the attributes column,
      deliberately kept `Map`-shaped in both modes so `MetricQueryService`'s ordinal-3
      fold loop needed zero changes; the per-type value expressions were left untouched,
      confirmed live to still aggregate correctly over the wider groups (a targeted test
      merged two differently-`host.name`'d rows sharing one `error.type` into a single
      series with `count` 1→3 and `value` reflecting the true `max-min` across all three,
      not just one arbitrary row). Missing-key data points collapse into one `"(none)"`
      series via ClickHouse's Map-subscript default-value behavior (no `mapContains`
      disambiguation - confirmed as the intended v1 behavior). Attribute-key discovery is
      a new `POST /api/metrics/attribute-keys` endpoint / `MetricAttributeKeysQueryBuilder`
      (`arrayJoin(mapKeys(DataPointAttributes))` grouped with `count(DISTINCT
      DataPointAttributes[Key])`), returning a per-key distinct-value-count cardinality
      hint (shipped in v1, not deferred) so the "Group by" picker can show e.g.
      "error.type (3)" vs "host.name (47)" before the user hits the cap - `MAX_SERIES`
      itself needed no code change, it already naturally shrinks with fewer returned
      series. The picker lives in `MetricsToolbar.svelte`, not `MetricChart.svelte` -
      it's filter-affecting (changes what SQL runs) like the service/compare controls
      already there, not a pure client reshape like `sumMode`/`histogramMode`. The chosen
      key is persisted in saved views (`MetricsFilterState.groupByAttributeKey`, same
      reasoning as `compareEnabled`) and auto-resets to ungrouped if a metric switch or
      saved-view restore lands on a metric without that key
      (`state.svelte.ts`'s `loadKnownAttributeKeys`). One real bug caught only by live
      verification (not unit tests, which only assert SQL text): `compactSeriesLabel`'s
      `?? '(none)'` fallback only caught a *missing* attribute key, not grouped mode's
      new case of a *present* key with an empty-string value - fixed to `|| '(none)'`.
      Live-verified end to end: posted synthetic OTLP Sum metrics through `docker
      compose`'s ingest container (including a data point missing `error.type`),
      confirmed `/api/metrics/attribute-keys`' discovered keys and counts, confirmed
      `/api/metrics/query`'s grouped-mode collapse and correct merged aggregation via the
      two-data-point test above, and drove the dashboard (Playwright) through selecting
      `error.type`, confirming the legend collapsed from 6 to 4 correctly-labeled lines
      (including the fixed `(none)` rendering), and reverting to "None" restored the
      original 6-series ungrouped view.
- [ ] **Research: a real "skip-index effectiveness" signal for the Indexing page.**
      2026-08-21, mid-redesign of the Indexing page's "Skip indexes" section into a
      "Query optimization" one (v9 above): the requested shape included an "X% of
      indexed queries benefited from data skipping" stat alongside the (shipped)
      search-latency percentiles and slow-query count. Deliberately **not** shipped -
      checked whether ClickHouse exposes this as real, queryable telemetry before
      building it, and it doesn't in a form this page can rely on. What was found:
      - `system.query_log`'s `ProfileEvents`/`SelectedMarks`/`SelectedRows`/etc. describe
        what was read *after* all pruning (primary key *and* skip indices combined) -
        there's no separate counter isolating the skip-index contribution specifically.
      - The one place ClickHouse does report per-index granule-drop counts
        ("Index `idx_name` has dropped X/Y granules") is a `LOG_TRACE`/`LOG_DEBUG` line,
        only reaches `system.text_log` if that table is enabled *and* the server's log
        level is raised well past the default - not something a self-hosted deployment
        can be assumed to have, unlike `part_log`/`query_log` (config-gated but at least
        commonly on).
      - `EXPLAIN indexes = 1` / `EXPLAIN ESTIMATE` show real per-index effectiveness, but
        only for a single ad-hoc query run right then - not retrospective over real
        historical dashboard traffic the way this stat needs to be.
      Open question for whoever picks this back up: is there a version-gated or
      config-gated ClickHouse mechanism (newer `system.query_log` columns, an opt-in
      trace setting worth documenting as a prerequisite, sampling live traffic through
      `EXPLAIN` on a schedule) that would make this honest rather than invented? If not,
      the fallback discussed and deferred at the same time was a differently-labeled,
      genuinely-computable proxy - e.g. "% of queries reading under N% of their table's
      total rows," a real read-efficiency signal from `system.query_log`, just not
      skip-index-specific (primary-key pruning contributes too, so it can't be labeled as
      the same claim).

### v4 — OTLP traces (the traces half of "OTLP traces & metrics")
Promoted out of "Later" and shipped 2026-08-10, in four passes (ingest+storage →
query API → dashboard UI → e2e verification), each independently verified before the
next began — the same staged-delivery discipline v3's Alerting item used. Metrics
deliberately excluded (see above); "Trace/log correlation view" also deliberately stays
a separate future item rather than being folded in here, after an earlier draft of this
plan proposed folding it in and that call was reversed for scope discipline.

- [x] **Ingest + ClickHouse storage** — vendored `trace.proto`/`trace_service.proto`
      (same pinned `v1.11.0` tag as the existing logs protos) and added gRPC
      (`OtlpGrpcTraceService`) + HTTP (`OtlpHttpTraceEndpoints`, `POST /v1/traces`)
      receivers sharing `Flare.Ingest`'s existing :4317/:4318 listeners.
      `OtlpTraceMapper` maps to a new `SpanRecord` model; a deliberately-duplicated
      (not generalized) Redis Streams → ClickHouse pipeline
      (`RedisStreamSpanEventSink` → `SpanFlushWorker` → `ClickHouseSpanWriter`) mirrors
      the logs pipeline's at-least-once/PEL-reclaim design. `db/clickhouse/0007_spans.sql`
      adds the `spans` table: `Events Nested(...)` for span events (ClickHouse desugars
      this into three parallel `Array(...)` columns) and `StatusCode Enum8(...)`.
      Both were spike-tested against a live `Aspire.ClickHouse.Driver` *before* being
      committed to (the single biggest identified risk in the whole effort) — confirmed
      `InsertBinaryAsync`/`ExecuteReaderAsync` round-trip them as plain
      `DateTime[]`/`string[]`/`Dictionary<string,string>[]` .NET values with no special
      handling. `ORDER BY (TraceId, StartTime, SpanId)` optimizes "get a full trace by
      id" (the waterfall) over "list spans by service+time," a known, deliberately
      unresolved trade-off documented in the migration and `db/clickhouse/README.md`,
      same spirit as `logs`' own unresolved `ORDER BY` trade-off. Span Links omitted
      entirely (schema and model) - add via a later `ALTER TABLE` once a concrete
      feature needs them. Unit tests added for `OtlpTraceMapper`/`ClickHouseSpanRowMapper`
      (`Flare.Ingest.Tests`), matching the coverage the logs pipeline already had.
      Verified end-to-end against a real `docker compose` stack: real OTLP trace
      exports via both HTTP/JSON and gRPC (`grpcurl` against the vendored proto)
      round-tripped correctly, including the `Events` Nested columns.
- [x] **Query API** (`Flare.Api`) — `Model/SpanFilter.cs` (time range, services, kinds,
      status codes, trace id, root-spans-only, duration range, attribute bags) and
      `Query/SpanFilterSqlBuilder.cs`/`SpanSearchQueryBuilder.cs`/`TraceByIdQueryBuilder.cs`
      (pure, parameterized, unit-tested), a new `SpanQueryService` (the one class
      holding `IClickHouseClient` for spans), and `Endpoints/SpanEndpoints.cs`:
      `POST /api/spans/search` (root-span list) and `GET /api/traces/{traceId}`
      (the waterfall's one query, ordered ascending by `StartTime`). Keyset pagination
      via `(StartTime, TraceId, SpanId)` - no synthetic id needed, unlike logs'
      `EventId`, since `(TraceId, SpanId)` is already spec-guaranteed unique. Verified
      via curl against real data: search pagination across pages, `Services`/`Kinds`/
      `StatusCodes`/duration-range filters, and correct parent-before-child trace-by-id
      ordering.
- [x] **Dashboard UI** (`src/dashboard`) — third nav entry (`AppNav.svelte`, first
      nested route in this app: `routes/traces/[traceId]`), `lib/traces-api.ts` +
      `lib/traces/state.svelte.ts`/`trace-state.svelte.ts` (list state, waterfall/detail
      state) following the established `$state` class + typed-context convention.
      `TraceList`/`TraceRow` mirror `LogTable`/`LogRow`; `SpanDetailSheet` mirrors
      `EventDetailSheet` and reuses `AttributeTable`. `TraceWaterfall` is the one
      genuinely new component in this dashboard - hand-rolled indented timeline bars
      (plain divs, no charting library), same "hand-roll it" precedent `VolumeChart`
      already set. No live-tail of spans (deliberately out of scope - the waterfall is
      the value, not a live firehose).
- [x] **End-to-end verification, 2026-08-10** — real `docker compose`-built images;
      Playwright-driven click-through (list → waterfall → span detail) against
      hand-built OTLP payloads first, then against a **real OTel SDK-produced trace**:
      extended `Aspire.Flare`'s client package (`AddFlareOtlpExporter`, previously
      logs-only) to also export traces, and `examples/ExampleApp.LogGenerator` to emit
      real child spans via `ActivitySource` nested under ASP.NET Core's
      auto-instrumented request span. That real run surfaced and fixed a genuine bug
      hand-built test data never would have caught: the waterfall's span-name label
      used `shrink-0` on the service-name suffix, which - combined with the real SDK's
      actual default `unknown_service:<name>` resource attribute (much longer than the
      short hand-crafted service names used earlier) - crowded the span name out to
      invisible width. Fixed the flex layout, rebuilt, re-verified visually. `dotnet
      test`: 174 tests, 0 failures (up from 121 at v3). `npm run build`/`svelte-check`:
      clean.

### v5 — Trace/log correlation view
Promoted out of "Later" and shipped 2026-08-10, same day as v4, as a single non-staged
session — v4 left this "nearly free" (existing `TraceId` bloom-filter index, existing
`/traces/{traceId}` route, `EventDetailSheet` already displaying the raw ids), so unlike
v3/v4 this didn't need multi-pass sequencing.

- [x] **Backend: `SpanId` on the existing log filter** — `Model/LogFilter.cs` /
      `Query/LogFilterSqlBuilder.cs` gained a `SpanId` field and equality clause,
      hand-mirroring `TraceId`'s exactly (same parameter-binding style, no dedicated skip
      index — `TraceId`'s point-lookup index does the heavy filtering, `SpanId` is a
      cheap equality check on the matched rows). No new endpoint, migration, or query
      builder class: `POST /api/logs/search` already accepted `LogFilter`. New unit test
      `Build_WithSpanId_AddsEqualityClause` mirrors `Build_WithTraceId_AddsEqualityClause`.
- [x] **Trace → linked logs** — `SpanDetailSheet.svelte` gained a "Linked logs" section
      (same slot/style as its existing Events section) that calls the existing
      `searchLogs` with `{ traceId, spanId }` on span selection and renders matching log
      lines inline (severity badge, timestamp, body) — no drill-down into the Logs
      Explorer, since that page has no URL/query-param-driven initial filter yet (a
      separate, larger piece of work). State is local to the sheet (`$state` +
      `$effect`, abort-on-reselect), not added to `TraceDetailState` — view-local,
      transient data with no other consumer.
- [x] **Log → trace link** — `EventDetailSheet.svelte`'s Trace ID field is now a plain
      `<a href="/traces/{traceId}">` when non-empty (falls back to `—` otherwise), same
      full-navigation primitive `SpanDetailSheet`'s existing "Back to traces" link uses.
- [x] **Verification** — `dotnet build`/`dotnet test`: 175 tests, 0 failures (up from 174
      at v4). `svelte-check`: clean, 0 errors across 858 files. Full docker-compose
      end-to-end (`POST /generate-burst` against a real correlated trace) and Playwright
      click-through intentionally not run for this item — the new `SpanId` path is a
      byte-for-byte mirror of the already e2e-verified `TraceId` path from v4, so the
      marginal confidence didn't justify the session cost; can be run on request.

### v6 — OTLP metrics (the metrics half of "OTLP traces & metrics")
Planned and shipped 2026-08-10. Split out of the traces item (see v4) because the OTLP
metrics data model is materially wider than the single span shape: a `Metric` carries one
of five point-type payloads (Gauge, Sum, Histogram, ExponentialHistogram, Summary), each
with its own fields, and metrics querying is fundamentally "get a time-bucketed series,"
not "search rows" the way logs/spans search is — no direct precedent to mirror there.
Staged the same way v4 was (ingest+storage → query API → dashboard UI → e2e verification),
each pass independently verified before the next began - two real bugs (one SQL
correctness bug in the query pass, one `ORDER BY`-after-`UNION ALL` bug) were caught by
that per-pass live verification, not by unit tests, the same payoff v4/v5 got from it.

**Scope decisions made up front** (both to keep this proportional to v4's effort, per the
2026-08-10 note that originally split metrics out):
- **Point types: Gauge + Sum + Histogram only.** Covers what .NET's built-in
  instrumentation actually emits by default (ASP.NET Core/`System.Runtime`/`HttpClient` —
  counters as Sum, durations as Histogram, observable gauges as Gauge).
  ExponentialHistogram (opt-in, rare in .NET) and Summary (legacy Prometheus-client-style
  quantiles) are deliberately deferred — same "add it when a concrete need exists"
  precedent v4 used to omit Span Links from spans. `OtlpMetricsMapper` recognizes both but
  drops their data points, logging once per metric name (rate-limited, not a hard error —
  one export commonly mixes point types and shouldn't fail wholesale over an unsupported
  one).
- **Ingest pipeline: one shared Redis stream + one flush worker, fanning out to three
  ClickHouse tables at flush time.** Logs vs. spans were deliberately duplicated into
  fully separate pipelines because they're different *signals*; gauge/sum/histogram are
  still one signal (metrics) arriving together in a single OTLP export, so splitting them
  into three parallel streams/workers/consumer-groups would be tripling operational
  surface for a sub-shape distinction, not a signal distinction. `MetricFlushWorker`
  reads one batch off `flare:metrics` and issues three `InsertBinaryAsync` calls (one per
  table), partitioning the batch by point type.

- [x] **Ingest + ClickHouse storage** — vendored `metrics.proto`/`metrics_service.proto`
      (same pinned `v1.11.0` tag as the existing logs/trace protos) and added gRPC
      (`OtlpGrpcMetricsService`) + HTTP (`OtlpHttpMetricsEndpoints`, `POST /v1/metrics`)
      receivers sharing `Flare.Ingest`'s existing :4317/:4318 listeners. `OtlpMetricsMapper`
      maps each `Metric` (name/description/unit/resource/scope) × its data points into one
      of `GaugePointRecord`/`SumPointRecord` (+`AggregationTemporality`/`IsMonotonic`)/
      `HistogramPointRecord` (+`Count`/`Sum`/`BucketCounts`/`ExplicitBounds`) — all three
      sharing one abstract `MetricPointRecord` base for their ~12 identical metadata
      fields, polymorphic over `System.Text.Json`'s `JsonPolymorphic`/`JsonDerivedType` so
      one Redis stream entry round-trips as whichever concrete point type it is.
      ExponentialHistogram/Summary points are recognized and dropped (not erroring the
      whole export), with a per-export warning log naming the affected metrics.
      `db/clickhouse/0008_metrics.sql` added three tables — `metrics_gauge`, `metrics_sum`,
      `metrics_histogram` — adapted from the OTel Collector's ClickHouse exporter default
      schema, same lineage as `logs`/`spans`. `ORDER BY (MetricName, ServiceName, Time)`
      per table (a metric's natural access pattern is "this metric, this service, over
      time," unlike spans' trace-id-first choice), with a bloom-filter skip index on
      `DataPointAttributes` keys/values only (no `ResourceAttributes` index — `ServiceName`
      already leads `ORDER BY`). Redis pipeline: single `flare:metrics` stream
      (`MetricEventPipelineOptions` mirroring `SpanEventPipelineOptions`'s shape),
      `RedisStreamMetricEventSink`, `MetricFlushWorker` (per the scope decision above —
      one shared stream/worker, partitioning the batch by runtime type into three
      `IClickHouseMetricWriter.WriteBatchAsync` inserts at flush time). Unit tests:
      `OtlpMetricsMapperTests` (18 cases covering all three point types, unsupported-type
      reporting, start-time-absent handling), `ClickHouseMetricRowMapperTests` (column
      order/coalescing per table), `MetricEventJsonContextTests` (polymorphic round-trip
      per concrete type) — 90 tests total in `Flare.Ingest.Tests`, 0 failures. **Verified end-to-end 2026-08-10** against a real `docker compose`
      stack (`clickhouse`+`redis`+`ingest`, fresh volumes so the init-mount picked up
      `0008_metrics.sql` automatically): confirmed all three tables exist, then sent real
      OTLP metric exports covering every mapped shape — HTTP/JSON: a Gauge (int value,
      attribute-tagged), a monotonic cumulative Sum (double value, distinct
      `StartTime`≠`Time`), a cumulative Histogram (bucket/bound arrays), and a Summary
      metric (confirmed dropped with the expected warning log, not stored); gRPC via
      `grpcurl` against the vendored proto: a Gauge, a **non-monotonic delta Sum with a
      negative value** (up/down counter shape), and a delta Histogram. Queried ClickHouse
      directly afterward and confirmed every field landed correctly, including
      `BucketCounts`/`ExplicitBounds` round-tripping as real `Array(UInt64)`/`Array(Float64)`
      values (the spike-test risk item — confirmed via this live run rather than a
      separate throwaway spike, since the full pipeline was quicker to stand up here than
      an isolated driver test) and `StartTime` correctly coalescing to `Time` when absent
      on the wire (gauge case) vs. preserved when present (sum case). `dotnet build
      Flare.sln` clean.
- [x] **Query API** (`Flare.Api`) — `Model/MetricModels.cs` (`MetricFilter`: time range,
      service, `DataPointAttributes`-only filter — no resource/scope-attribute filtering,
      unlike `SpanFilter`'s three bags, since no planned query needs it). `POST
      /api/metrics/names` lists distinct `(MetricName, Type, Unit, Description)` tuples
      for a service + time range (metrics discovery — no log/span equivalent needed,
      since those are searched, not picked from a list; POST+body, not GET, for
      consistency with every other structured-filter endpoint in this API — a plan-time
      GET sketch was reconsidered during implementation). `POST /api/metrics/query` is
      the core, genuinely new endpoint: given a metric name + explicit `Type` (the
      caller already knows it from a prior `/names` response) + filter + bucket `step`,
      returns a time-bucketed series per attribute-set (`MetricSeries`, series key =
      `toString(DataPointAttributes)` grouping, not the `Map` column directly — sidesteps
      relying on unconfirmed `Map`-column `GROUP BY` semantics for something a plain
      string trivially guarantees) — Gauge as `avg(Value)` per bucket; Sum (cumulative,
      monotonic = counter) as `max(Value) - min(Value)` per bucket; Histogram as
      `sum(Count)`/`sum(Sum)` plus `sumForEach(BucketCounts)` (the aggregate-combinator
      that sums arrays element-wise across grouped rows) fed through
      `HistogramQuantileEstimator` for p50/p90/p99 via linear interpolation over
      `BucketCounts`/`ExplicitBounds` (the standard Prometheus/Grafana
      `histogram_quantile` approximation, with a `null`-not-throw fallback for malformed/
      empty bucket data). **Two named, deliberately-unresolved v1 limitations**, flagged
      rather than solved now (same convention the schema docs already use elsewhere): a
      counter reset/process-restart mid-bucket, or a bucket narrower than the sample
      interval (confirmed live: a single sample per bucket always reads as a zero rate,
      since `max - min` needs ≥2 points), breaks the naive rate calc; the quantile
      interpolation assumes the metric's bucket boundaries don't change mid-window.
      `Endpoints/MetricsEndpoints.cs`, `MetricQueryService`, SQL builders
      (`MetricFilterSqlBuilder`/`MetricNamesQueryBuilder`/`MetricSeriesQueryBuilder`)
      following `SpanFilterSqlBuilder`/`SpanSearchQueryBuilder`'s naming convention. New
      unit tests across the SQL builders and `HistogramQuantileEstimatorTests` (the
      quantile-interpolation math as pure functions — the easiest and highest-value thing
      to unit test in this pass) — 153 tests total in `Flare.Api.Tests`, 0 failures.
      **Verified end-to-end 2026-08-10** against a real `docker compose` stack
      (`clickhouse`+`redis`+`ingest`+`api`): sent real OTLP exports (a two-series Gauge,
      a 3-point cumulative Sum, two Histogram data points landing in the same bucket) via
      curl, then queried `/api/metrics/names` and `/api/metrics/query` for all three
      point types — histogram count/sum/bucket-merge/quantiles, gauge per-series
      splitting, and the sum rate calc at both a narrow (single-sample, correctly reads
      0) and wide (multi-sample, correctly reads the true delta) bucket width all matched
      hand-computed expected values, plus 400s for a non-positive bucket width and a
      missing required field, and an empty (not error) response for an unknown metric
      name. **Found and fixed one real bug this live run caught that unit tests
      couldn't**: `MetricNamesQueryBuilder`'s trailing `ORDER BY MetricName` after a
      chain of `UNION ALL` SELECTs bound only to the last branch, not the combined
      result (confirmed via a direct `clickhouse-client` query before and after) —
      fixed by wrapping the three branches in a subquery and applying `ORDER BY` outside
      it. `dotnet build Flare.sln` clean.
- [x] **Dashboard UI** (`src/dashboard`) — fourth nav entry (`AppNav.svelte`): "Metrics".
      `lib/metrics-api.ts` + `lib/metrics/state.svelte.ts`/`context.ts` following the
      established `$state` class + typed-context convention (mirrors
      `TracesExplorerState` closely, including its `loadKnownServices` wide-window
      workaround for populating the service filter without self-narrowing). `MetricPicker`
      — a persistent (not popover) `Command`-based filterable list from
      `/api/metrics/names`, one row per (metric name, service) pair with a type badge —
      + `MetricChart`, a hand-rolled multi-line chart reusing `VolumeChart`'s core SVG
      technique (viewBox, array-index x-positions rather than a real time scale, hover-
      to-tooltip): value-over-time per series for Gauge, `max-min`-rate-over-time per
      series for Sum (both overlay every series as its own line, capped at 5 - the
      `dataviz` skill's validated categorical palette's slot count - with a "+N not shown"
      note past that, never a cycled/generated color), and p50/p90/p99 for Histogram
      (one series' distribution at a time, picked via a dropdown when a histogram metric
      has more than one series - N series × 3 lines each was judged unreadable). No
      live-tail (same "the chart is the value, not a firehose" reasoning that kept spans
      live-tail out of v4) and no dashboards-as-code / arbitrary panels (existing
      non-goal). `svelte-check`: 0 errors. `npm run build`: clean.
      **Found and fixed one real correctness bug while building this pass, before any
      UI code touched it**: `MetricSeriesQueryBuilder`/`MetricNamesQueryBuilder` (pass 2)
      grouped series by `DataPointAttributes` alone, not also `ServiceName` - two
      different services emitting the same metric name with the same (or no) data-point
      attributes would have silently merged into one line/one picker entry. Fixed by
      adding `ServiceName` to both queries' `GROUP BY`/`ORDER BY` and to the
      `MetricNameInfo`/`MetricSeries` response DTOs (`MetricSeries.serviceName`,
      `MetricNameInfo.serviceName` - the picker's selection key is now the
      (metricName, serviceName) pair, not metric name alone); `Flare.Api.Tests` updated
      to match (154 tests, 0 failures) — re-verified against real ClickHouse data deferred
      to Pass 4 per its own broader real-SDK verification, not re-run standalone here.
      **Theme change**: `layout.css`'s `--chart-1..5` tokens - inherited from the shadcn
      scaffold as a flat 0-chroma grayscale ramp, never actually used since `VolumeChart`
      is single-series - replaced with the `dataviz` skill's validated categorical
      palette (fixed blue/orange/aqua/yellow/magenta order, a CVD-safety property) for
      both light and dark, confirmed via its validator script against both this theme's
      surfaces (dark: all pass; light: WARN on 3 slots' contrast, mitigated by
      `MetricChart` always pairing color with a visible text legend, never color alone).
- [x] **End-to-end verification, 2026-08-10** — extended `Aspire.Flare`'s
      `AddFlareOtlpExporter` (logs+traces before this pass) to also register a named
      OTLP metrics exporter (`.WithMetrics(m => m.AddOtlpExporter(connectionName, ...))`),
      and gave `examples/ExampleApp.LogGenerator` a real `Meter` (`ExampleApp.LogGenerator`,
      registered via `AddMeter` alongside its existing `AddSource` call) with one
      instrument per v1 point type, all tied to the same `GenerateBurst` path its
      `ActivitySource` already instruments: `loggenerator.bursts` (`Counter<long>`, Sum),
      `loggenerator.burst.duration` (`Histogram<double>`, Histogram),
      `loggenerator.last_burst_size` (`ObservableGauge<int>`, Gauge). Real `docker
      compose`-built full stack (`clickhouse`+`redis`+`ingest`+`api`+`dashboard`); the
      log generator run standalone (not via Aspire, same as `run-full-stack.sh`) against
      it, driven by several `POST /generate-burst` calls of varying size over time.
      Confirmed via direct ClickHouse queries first: real ASP.NET Core/HttpClient/.NET
      runtime instrumentation (already wired by `Flare.ServiceDefaults.ConfigureOpenTelemetry()`'s
      existing `AddAspNetCoreInstrumentation()`/`AddRuntimeInstrumentation()`) landed
      across all three tables alongside the three custom instruments, dozens of distinct
      metric names total, correctly typed. Then a full Playwright click-through against
      the real running dashboard (not hand-built payloads first this time - the built-in
      instrumentation alone already supplied enough real, varied data that a hand-built-
      payload pass would have added little before real SDK data was available anyway):
      nav entry, toolbar, picker (filter-as-you-type, correct type badges, correct
      service scoping), and the chart for all three point types - `loggenerator.burst.duration`
      rendered three distinct p50/p90/p99 lines with a working legend, `loggenerator.bursts`
      and `loggenerator.last_burst_size` each rendered their single line correctly (no
      legend shown for one series, matching the `dataviz` skill's rule), and the service
      filter correctly narrowed the picker to one service while keeping the current
      chart selection since it stayed in scope. Zero browser console errors throughout.
      **Re-verified the `ServiceName`-grouping fix from Pass 3** (deferred there per
      request) with two freshly-inserted services sharing one metric name
      (`shared.request.count` on `service-a` and `service-b`, real "now" timestamps):
      `/api/metrics/names` correctly listed them as two separate entries, and
      `/api/metrics/query` with no service filter returned two separate series rather
      than one merged one - confirmed the two services' distinct values never combined.
      `dotnet build Flare.sln`/`dotnet test`: 244 tests (90 `Flare.Ingest.Tests` + 154
      `Flare.Api.Tests`), 0 failures. `svelte-check`: 0 errors. `npm run build`: clean.
      Stack torn down after verification (`docker compose down`, volumes kept).

### v7 — Saved views ("Saved dashboards / shareable views")
Promoted out of "Later" and shipped 2026-08-10. Scoped up front to **named, reloadable,
shareable filter/selection state per explorer page** (Logs/Traces/Metrics), not a
Grafana-style multi-panel dashboard builder — `VolumeChart`/`MetricChart` are hard-wired
to their own page's context, not props-driven reusable panels, and a real builder would
directly conflict with this doc's own "dashboards-as-code, arbitrary user-built panels"
non-goal. Views are global/unowned (no auth exists anywhere yet, same as `alert_rules`).

- [x] **ClickHouse storage + API** — `db/clickhouse/0009_saved_views.sql`: `saved_views`
      table, reusing `alert_rules`' `ReplacingMergeTree(UpdatedAt)` + `IsDeleted`
      tombstone CRUD pattern verbatim. `PageType LowCardinality(String)` lets one
      table/endpoint set serve all three pages; `StateJson` is fully opaque to
      Flare.Api (round-tripped as `JsonElement`, never interpreted — one level more
      opaque than `alert_rules.ConditionJson`, which at least deserializes into a real
      `LogFilter`). `Model/SavedViewModels.cs`, `Json/SavedViewsJsonContext.cs`,
      `Query/SavedViewQueryService.cs`, `Endpoints/SavedViewEndpoints.cs` under
      `/api/views` (`POST`/`GET` (+optional `pageType` filter)/`GET {id}`/`PUT {id}`/
      `DELETE {id}`) — a near 1:1 mirror of the Alerting CRUD stack.
- [x] **Dashboard: per-page save/load + a "Views" management page** — each explorer
      state class (`LogsExplorerState`/`TracesExplorerState`/`MetricsExplorerState`)
      gained `toSavedViewState()`/`applySavedViewState()`; a shared `ViewsMenu.svelte`
      toolbar control (list + "Save current view…") was added to all three toolbars
      rather than three bespoke ones. New `/views` route (`SavedViewsState`,
      `SavedViewTable.svelte`, rename-only `RenameViewDialog.svelte` — a view's filter
      itself is only edited from its source page) mirrors the Alerts page's
      state-class/context/Dialog+Table shape. Fifth `AppNav.svelte` entry.
- [x] **Shareable links, scoped narrowly** — a one-shot `?view=<id>` query param read
      only in each explorer's `onMount` (`lib/saved-views/hydrate.ts`), not the
      generalized "every filter change reflects into the URL" work this doc's v5
      section flagged as separate and still unstarted. "Copy link" on the `/views`
      table writes `${origin}${pageBasePath}?view=<id>` to the clipboard.
- [x] **Verified end-to-end, 2026-08-10** — `dotnet build`/`dotnet test`: 249 tests (up
      from 244), 0 failures (new `SavedViewRequestJsonTests`). `svelte-check`: 0 errors
      across 880 files. `npm run build`: clean. Real `docker compose`-built stack: full
      `curl` CRUD round-trip against `/api/views` (create per page type, `pageType`-filtered
      list, get, update, soft-delete, 404 on a deleted id); Playwright click-through
      against the real running dashboard — saved a live Logs filter via the toolbar,
      confirmed it listed on `/views`, "Copy link"/"Open" produced and followed a
      working `?view=` URL, a **fresh tab** opened directly on that URL correctly
      reproduced the filter with live tail off, and delete removed it from both
      `/views` and the toolbar's own list. Zero browser console errors throughout.
      **Found one real, pre-existing bug this live run caught, unrelated to this
      feature**: `Flare.Api`/`Flare.Ingest`'s Dockerfiles never `COPY db/clickhouse/`
      into the build context, so `Flare.ServiceDefaults.csproj`'s
      `EmbeddedResource Include="..\..\db\clickhouse\*.sql"` glob silently matches zero
      files when built via `docker build` (confirmed: the container's published
      `Flare.ServiceDefaults.dll` has zero embedded resources, vs. 9 when built via
      plain `dotnet build` locally) — `ClickHouseMigrationRunner` therefore never
      actually applied a migration from the real Docker images, the exact "past its
      first boot" scenario its own doc comment says it exists to fix. `saved_views`
      itself still verified correctly in this pass because that first pass was a fresh
      volume, so `docker-entrypoint-initdb.d`'s separate mount-based mechanism created
      it directly - the runner bug stayed latent.
- [x] **Follow-up: fixed the Dockerfile bug above, confirmed live against the real
      `xracer007/flare-api:edge` example, 2026-08-10** — caught in the wild, not just
      theorized: running `examples/ExampleApp.AppHost` against its own (non-fresh,
      reused-volume) ClickHouse, the dashboard's `/views` page showed a raw "Failed to
      fetch" for every request. `flare-api`'s real console logs (via the `aspire` MCP
      tools) showed the actual cause one layer down: `ClickHouse.Driver.ClickHouseServerException:
      Unknown table expression identifier 'saved_views' (UNKNOWN_TABLE)` - a clean `500`
      that the browser couldn't read because `Flare.Api` has no exception-handling
      middleware, so the unhandled exception unwound past `UseCors()` without it adding
      response headers, and CORS-header-less responses are invisible to `fetch()`
      (surfaces as the generic, otherwise-undebuggable "Failed to fetch" - a separate,
      still-open gap affecting *any* unhandled exception on *any* endpoint, not fixed
      here, not scoped to this pass). The actual fix: both Dockerfiles now
      `COPY db/clickhouse/ db/clickhouse/` into the build stage before `dotnet publish`.
      Verified by extracting the rebuilt image's `Flare.ServiceDefaults.dll` and
      confirming `GetManifestResourceNames()` now returns all 9 `.sql` files (was empty
      before), then a full fresh `docker compose up -d --build`: `schema_migrations`
      went from 0 rows to 9 (one per migration - the direct, only-possible-via-the-runner
      proof, since `docker-entrypoint-initdb.d` never populates that table itself), and
      `GET /api/views` returned a clean `200 {"views":[]}`.
- [x] **Follow-up: "Saved searches" reskin on the Logs page, 2026-08-17** — prompted
      by the user doubting whether "Saved views" on Logs was anything other than a
      saved search under a generic name; it wasn't — `LogsExplorerState`'s
      `toSavedViewState()`/`applySavedViewState()` already captured exactly time
      range + services + severity + search text, the same 5 fields a "saved search"
      needs, just labeled "Views" because the control (`ViewsMenu.svelte`/
      `SaveViewDialog.svelte`) is shared verbatim across Logs/Traces/Metrics. Decided
      (with the user) to reskin rather than build new backend surface: new
      Logs-only `SavedSearchesMenu.svelte`/`SaveSearchDialog.svelte`
      (`src/dashboard/src/lib/components/logs/`) replace `ViewsMenu`/
      `SaveViewDialog` on `LogsToolbar.svelte` only (star icon, "Saved searches" /
      "My saved searches" / "Save current filter…" copy) - Traces/Metrics keep the
      original "Views" control untouched. Same `saved_views` ClickHouse table and
      `/api/views` API underneath (`pageType: "Logs"`), zero backend changes. Only
      new behavior: inline per-row delete in the popover (native `confirm()` +
      `deleteSavedView`, mirroring `SavedViewTable.svelte`'s delete pattern) so a
      saved search can be removed without leaving the Logs page - rename still only
      via the existing `/views` page. No seeding: fresh installs start with an empty
      list. The `/views` management page and the `?view=<id>` shareable-link
      hydration (`lib/saved-views/hydrate.ts`) both keep working unchanged for
      Logs-tagged rows. Verified via `svelte-check` (0 errors) and `npm run build`
      (clean); full live Docker click-through deliberately skipped for this pass
      (low-risk, near-verbatim mirror of the already e2e-verified v7 code path) -
      user can spot-check next time the stack is running.

### v8 — Ingestion page (MVP)
Prompted by user comparison to Seq's own Ingestion page (2026-08-10) — asked whether Flare
could have one, "or even a better one." Scoped in two passes on purpose: this MVP
(throughput + rejected payloads) shipped same-day; **pipeline health** (Redis stream
lag, flush-worker status, per-service breakdown) was deliberately deferred to "Later"
above rather than folded in, since Seq has no equivalent of it (Flare's buffered
Ingest→Redis→ClickHouse pipeline is the one thing Seq's single-process model doesn't
need to expose) and it's a materially separate design pass.

- [x] **Ingest-side stats tracking** (`src/Flare.Ingest/Stats/`) — `IIngestionStatsTracker`/
      `RedisIngestionStatsTracker`, reusing the same `IConnectionMultiplexer` the event
      sinks already hold rather than adding new infrastructure. One Redis hash per
      wall-clock minute (`flare:ingestion:minute:{epochMinute}`, 25h TTL — a full 24h
      window plus a 1h buffer), fields keyed `{signal}:{protocol}:{requests|records|bytes|rejected}`
      (`logs:http:records`, etc. — plain lowercase, readable directly via `redis-cli
      HGETALL` while debugging, same rationale `LogEventJsonContext` documents for its
      own plain-property-name choice). A capped (200-entry) `flare:ingestion:errors` list
      backs the dashboard's "Ingestion Log" panel. Both writes go through one `IBatch` per
      call (single Redis round trip regardless of counter count), since this runs on the
      hot ingest path. `IngestionStatsKeys` holds the pure key/field-naming logic,
      split out so it's unit-tested without a real/mocked Redis connection.
- [x] **All six OTLP endpoint/service classes instrumented** (`Otlp/*.cs` — HTTP + gRPC ×
      Logs/Traces/Metrics) — `RecordAcceptedAsync` (record count + real byte count: buffer
      length for HTTP protobuf, `Encoding.UTF8.GetByteCount` for HTTP JSON, `CalculateSize()`
      for gRPC) on success, `RecordRejectedAsync` (reason string, no payload content) on an
      unsupported content type or a parse failure. **Found and fixed a real, pre-existing
      bug while wiring this**: the three HTTP endpoints had no try/catch around
      `JsonParser.Parse`/`MessageParser.ParseFrom` at all — a malformed body took the
      whole request down with a bare unhandled 500 and left no record anywhere that it
      happened. Now: a clean `400`, a `LogWarning` trail, and a counted+listed entry on
      the Ingestion page. gRPC's three services get the equivalent treatment
      (`RpcException(StatusCode.InvalidArgument)` instead of a bare 400, since gRPC has
      already deserialized the wire message by the time the handler runs — there's
      nothing left to fail on except the mapping step).
- [x] **Query API** (`Flare.Api`) — `GET /api/ingestion/stats?minutes=60` (a plain GET
      with one bounded query param, not the POST+JSON-body convention every other
      structured-filter endpoint in this API uses — there's no real filter object here to
      justify a body). `IngestionStatsQueryService` reads back every minute-bucket hash in
      the requested window plus the recent-errors list as a single batched round trip
      (dispatches all `HGETALL`s + the one `LRANGE` together via `IDatabase.CreateBatch`,
      confirmed via live testing at the worst case: a full 1440-bucket 24h window resolves
      in ~60ms). Deliberately duplicates (doesn't reference) `Flare.Ingest`'s
      `IngestionSignal`/`IngestionProtocol`/key-format logic — same "different
      deployables, no project reference between them" precedent every other
      Flare.Ingest/Flare.Api pairing in this repo already follows. Response is dense (every
      minute × all 6 signal/protocol pairs, zero-filled) so the dashboard never gap-fills
      by timestamp itself. `totals.arrivalsPerMinute`/`ingestedRecordsPerMinute`/
      `ingestedBytesPerMinute` read only the single most recent (possibly still-filling)
      bucket - a real distinction (requests vs. the records/bytes within them), not a
      cosmetic split of the same number the way a literal mirror of Seq's "Current
      Arrivals"/"Current Ingestion" tiles would have been, since Flare counts a request
      "arrived" and "ingested" at the same instant (no auth/gateway layer to separate
      them yet). `totals.requestsInWindow`/`rejectedInWindow` sum over the same window as
      the chart, not a hardcoded 24h like Seq's own tiles - keeps this to one Redis round
      trip per call rather than always paying for a 1440-bucket scan regardless of the
      selected window. The pure aggregation logic (`BuildBuckets`/`BuildTotals`/
      `BuildRecentErrors`) is `internal` and unit-tested directly against hand-built
      `HashEntry[]`/`RedisValue[]` data, no mocked Redis connection needed.
- [x] **Dashboard: Ingestion page** (`src/dashboard`) — sixth nav entry (`AppNav.svelte`).
      `lib/ingestion-api.ts` + `lib/ingestion/state.svelte.ts`/`context.ts` follow the
      established `$state` class + typed-context convention, with one new wrinkle: this is
      the first explorer page that polls (10s interval) rather than relying on live-tail
      or a manual refresh - matches what actually makes an ingestion page valuable ("is
      something wrong right now"), while staying an aggregate poll rather than a
      per-event firehose (same "live-tail is reserved for genuine per-event streams"
      precedent that kept spans/metrics live-tail out of v4/v6). Four summary tiles
      (Card component's first real use in this app), a hand-rolled 3-line chart
      (`IngestionChart.svelte`, events/minute by signal, reusing `MetricChart`'s
      viewBox/array-index-x/hover-tooltip technique), a six-row per-(signal, protocol)
      table (`IngestionSignalsTable.svelte` - Flare's analog to Seq's per-API-key input
      list, since Flare has no auth/named-input concept yet), and an inline "Ingestion
      Log" table of recent rejections (`IngestionLog.svelte` - inline rather than a
      separate route, since the list is already small and this is the only page that
      would ever link to it).
- [x] **Verified end-to-end, 2026-08-10** — `dotnet build`/`dotnet test`: 271 tests (100
      `Flare.Ingest.Tests` + 171 `Flare.Api.Tests`, up from 244/154), 0 failures.
      `svelte-check`: 0 errors across 890 files. `npm run build`: clean. Real
      `docker compose up -d --build` (clickhouse+redis+ingest+api+dashboard): sent real
      OTLP exports via curl - a valid HTTP/JSON log and a valid HTTP/JSON trace (200,
      correctly counted with real byte/record numbers), a malformed JSON body (400,
      recorded as `invalid-payload:InvalidJsonException`), an unsupported content type
      (415, recorded as `unsupported-media-type`), and a malformed protobuf body on
      `/v1/traces` (400, recorded as `invalid-payload:InvalidProtocolBufferException`).
      Confirmed all of it via `GET /api/ingestion/stats` directly, then via a real
      Playwright-driven Chromium browser against the live dashboard: tiles, chart,
      per-receiver table, and ingestion log all showed the exact expected numbers/reasons,
      the window-preset select correctly listed and switched between all four presets
      (including the 24h/1440-bucket worst case, confirmed fast), and zero browser console
      errors throughout.

### v9 — Indexing page
Prompted by the same Seq-comparison conversation as v8, same day. **Deliberately not a
literal port of Seq's own Indexing page** - naming and scope were both reworked to fit
ClickHouse's actual model rather than Seq's:
- **Naming**: considered "Storage" first, rejected - collides with the *other*
  Later-roadmap item ("Retention policies + cold storage to S3-compatible object store
  (RustFS)"), which will need its own page eventually. Kept "Indexing" (Seq's own name)
  instead, so the vocabulary stays non-overlapping long-term: **Indexing** = ClickHouse-
  local/hot ("how is my data organized"), **Retention** = RustFS/S3-cold ("how long do I
  keep it"), whenever that item gets built.
- **Scope**: Seq's Indexing page exists because its embedded store makes *you* explicitly
  create computed/signal indexes and pay their storage/CPU cost - the page is a tuning
  tool. ClickHouse already made those tradeoffs at the schema level (`db/clickhouse/*.sql`,
  17 skip indexes across `logs`/`spans`/`metrics_*` as of this item), so there's nothing
  for a self-hosted user to create or tune here - a literal λ/signal-list clone would be
  read-only trivia at best, misleading at worst. Rebuilt instead as: per-table storage/
  compression (an operator question almost no self-hosted ClickHouse user has a UI for),
  the skip-index inventory (makes the schema work in `db/clickhouse/*.sql` visible, not
  editable), and a growth trend - genuinely more useful than Seq's page for this product's
  actual architecture, not just a reskin of it.

- [x] **Live research before writing any query** (2026-08-10) — spun up the real
      `clickhouse`/`redis` containers first and probed `system.tables`/`system.parts`/
      `system.data_skipping_indices`/`system.part_log` directly via `clickhouse-client`
      before committing to a query shape, same "verify against the real thing, don't
      assume" precedent the dotnet-otel-grpc-gotchas memory documents elsewhere in this
      project. Caught two things a docs-only read would have gotten wrong: (1)
      `system.data_skipping_indices` has no `marks` column - it's `marks_bytes`; (2)
      `system.parts`' own `data_uncompressed_bytes` excludes index/mark overhead and reads
      *smaller* than the compressed size at low row counts (confirmed live: 363 B
      "uncompressed" vs. 924 B compressed on a 1-row table) - actively misleading for a
      compression-ratio stat. Switched to `system.tables.total_bytes`/
      `total_bytes_uncompressed`/`active_parts`, which already carries every per-table
      number needed in one row, no join against `system.parts` required at all - simpler
      than the originally planned query, not just a corrected one.
- [x] **Query API** (`Flare.Api`) — `GET /api/indexing/stats`, no request body/params:
      `IndexingQueryService` runs three independent, fixed queries concurrently
      (`system.tables`, `system.data_skipping_indices`, and a 30-day-bounded
      `system.part_log` scan for the growth trend), all scoped to `WHERE database =
      currentDatabase()` - Flare owns its whole ClickHouse database, so no table allowlist
      is hardcoded; a future migration's new table shows up automatically. No SQL-builder
      class the way `LogQueryService`/`SpanQueryService` have one, since there's no
      per-request filter to translate - the query shape is fixed. The growth query is
      wrapped in try/catch: `system.part_log` is config-gated, not guaranteed on every
      self-hosted ClickHouse deployment, so a failure there degrades to
      `growthAvailable: false` rather than failing the whole response.
- [x] **Dashboard: Indexing page** (`src/dashboard`) — seventh nav entry (`AppNav.svelte`).
      `lib/indexing-api.ts` + `lib/indexing/state.svelte.ts`/`context.ts` follow the
      established convention, with one deliberate difference from every other explorer
      page: no time-window selector and no polling (`IngestionState`'s 10s poll doesn't
      fit here - table/index shape doesn't change on a 10-second cadence) - just a load-
      once-plus-manual-refresh button, matching what this data actually is. Four summary
      tiles, a hand-rolled 5-line growth chart (`IndexingGrowthChart.svelte`, top 5 tables
      by size, reusing `IngestionChart`'s technique), a per-table storage/compression
      table, and a skip-index inventory table. `lib/indexing/format.ts` re-exports
      `formatBytes`/`formatCount` from `$lib/ingestion/format` rather than duplicating them
      (both pages want identical formatting) and adds one new pure helper,
      `formatRatio`.
- [x] **Found and fixed a real bug during Playwright verification, before any code was
      merged** — `IndexingGrowthChart`'s conditional order checked
      `!stats?.growthAvailable` *before* checking whether the page had finished loading at
      all, so every fresh page load flashed the "not available - `system.part_log` isn't
      queryable" message for the ~1-2s the first fetch was in flight (`stats` is `null`
      until then, and `null?.growthAvailable` is falsy same as an explicit `false`) -
      caught live by taking a snapshot mid-load, not by reasoning about the code
      statically. Fixed by checking the loading state first.
- [x] **Verified end-to-end, 2026-08-10** — `dotnet build`/`dotnet test`: 271 tests, 0
      failures (unchanged from v8 - no new pure/testable logic this endpoint's fixed,
      argument-free queries introduced, consistent with `LogQueryService`/
      `SavedViewQueryService` also having no dedicated unit tests beyond their SQL-builder
      siblings). `svelte-check`: 0 errors across 903 files. `npm run build`: clean. Real
      `docker compose up -d --build` (clickhouse+redis+ingest+api+dashboard, reusing the
      v8 session's already-populated volumes plus a few more OTLP metric points sent
      live): `GET /api/indexing/stats` confirmed correct against direct
      `clickhouse-client` queries run beforehand (9 tables, 17 skip indexes, matching
      byte-for-byte). Playwright-driven Chromium session against the live dashboard:
      tiles, growth chart (5-series legend + "+4 more" note), tables table (correct
      compression ratios, e.g. `logs` at 12.6x), and skip-index table all rendered
      correctly; the loading-order bug above caught and fixed within this same pass,
      re-verified after the fix; zero browser console errors throughout.

### v10 — Ingestion page: pipeline health
Promoted out of "Later" and shipped 2026-08-10, same day as v8/v9. Extends the existing
`/ingestion` page (v8) with a lower section, not a new nav entry - the item's own title is
"Ingestion page," not a new page - answering "is the buffered pipeline keeping up" where
v8's tiles/chart/table answer "is data arriving."

**Live research before writing any code** (same "verify against the real thing" precedent
v9's own opening step used) - spun up a throwaway `redis:latest` (8.10.0) container and
probed `XADD`/`XGROUP CREATE`/`XREADGROUP`/`XINFO GROUPS`/`XPENDING` directly via
`redis-cli` before committing to a design. This changed the plan in one concrete way:
`XINFO GROUPS` returns a `lag` field Redis tracks natively (entries-added vs.
entries-read), and `StackExchange.Redis` 2.13.x already exposes it typed
(`StreamGroupInfo.Lag`, via `IDatabase.StreamGroupInfoAsync`) - a materially better
"is the pipeline falling behind" number than the originally-sketched `XPENDING`-count
proxy the Later-item bullet above described. `XPENDING`'s own count/idle-time is still
used, but for a different question ("is something stuck" - delivered but never acked),
not as a lag substitute.

**Scope decisions made up front:**
1. **Stream/consumer-group health is read live from Redis by `Flare.Api`, not tracked in a
   hash the way the other two are.** Redis already holds this state natively (`XLEN`/
   `XINFO GROUPS`/`XPENDING`); writing a duplicate copy into a stats hash would just be
   another thing that can drift. `Flare.Api` already held its own `IConnectionMultiplexer`
   (`IngestionStatsQueryService`, v8), so no new infrastructure dependency either.
2. **Flush-worker health needed a genuinely new Ingest-side tracker**, since it's the one
   piece of this feature with no natural home in Redis already: each `*FlushWorker`'s own
   last-flush timestamp/batch size/error state is in-process data. New
   `IFlushHealthTracker`/`RedisFlushHealthTracker` (`src/Flare.Ingest/Stats/`), one Redis
   hash per signal (`flare:ingestion:flush:{signal}`, no TTL - unlike the per-minute stats
   buckets, this must keep showing the *last known* outcome even after a worker stops
   updating it, which is exactly the "why did my logs stop showing up" case this item
   exists to diagnose), updated once per flush cycle (not the hot per-request path, so no
   `IBatch`-per-call pressure the way v8's `IIngestionStatsTracker` has).
3. **Per-`service.name` breakdown reuses `service.name` already present on every mapped
   record** (`LogEvent`/`SpanRecord`/`MetricPointRecord.ServiceName`, from the v4/v6
   pipelines) rather than re-parsing the OTLP resource. New `ServiceBreakdown.Build` (pure,
   unit-tested) groups accepted records by service and splits the request's one byte count
   *proportionally* to each service's record share - a documented approximation, not exact
   accounting: OTLP gives one byte count per whole export request, not per resource, so an
   export mixing services' records has no way to be measured exactly. Written via a new
   `IIngestionStatsTracker.RecordServiceBreakdownAsync` method (not folded into the
   existing `RecordAcceptedAsync` signature) to two per-(minute, signal) hashes -
   `flare:ingestion:service-records:{minute}:{signal}` / `...-bytes:...`, field = raw
   service name - deliberately *not* packed into a composite field name the way v8's
   `{signal}:{protocol}:{counter}` fields are, since a service name can itself legally
   contain a colon and this avoids any delimiter-collision risk parsing it back. Top-N
   selection (`PipelineQueryService.TopServicesPerSignal = 5`, same slot count the
   `dataviz` skill's palette already caps `MetricChart`/`IndexingGrowthChart` at) happens
   at query time, not write time, with the rest folded into an "+N more" total - not a
   silent drop.
4. ~~**Known, deliberately-unresolved limitation** (flagged, not solved, same convention
   v6's rate-calc/quantile-interpolation notes use): `PipelineStreamKeys` (`Flare.Api`)
   hardcodes each signal's stream key/consumer-group name as `LogEventPipelineOptions`/
   `SpanEventPipelineOptions`/`MetricEventPipelineOptions`'s *default* values
   (`flare:logs`/`flare-ingest`, `flare:spans`/`flare-ingest-spans`,
   `flare:metrics`/`flare-ingest-metrics`) - a deployment that overrides them via its own
   config won't have its stream health picked up here, since the two processes share no
   config source (same "different deployables, no reference" situation every other
   Flare.Ingest/Flare.Api pairing is already in).~~ **Fixed 2026-08-19 (code+build+unit-test
   verified; live e2e via `docker compose` still pending - Docker Desktop wasn't running on
   this machine).** `PipelineStreamKeys` is now an injectable instance class that resolves
   each signal's stream key/consumer group from Flare.Api's own new, minimal, bound copies of
   Flare.Ingest's pipeline options (`Flare.Api.Pipeline.LogEventPipelineOptions`/
   `SpanEventPipelineOptions`/`MetricEventPipelineOptions` - only the two fields Flare.Api
   reads, not Ingest's full tuning surface) instead of hand-copied literals - bound from the
   *same* `LogEventPipeline`/`SpanEventPipeline`/`MetricEventPipeline` config sections
   Flare.Ingest binds, so a `LogEventPipeline__StreamKey` (etc.) env-var override now reaches
   `PipelineQueryService` if applied to Flare.Api's environment too. While researching this
   fix, found the identical root-cause bug a second time, independently: `LiveTailOptions.
   StreamKey` was bound from its own, differently-named `LiveTail` section rather than
   `LogEventPipeline`, so `LogTailBroadcaster` (live-tail) was also blind to the same
   override, via a different mechanism - fixed the same way, `LogTailBroadcaster` now sources
   its stream key from `Flare.Api.Pipeline.LogEventPipelineOptions` directly, and
   `LiveTailOptions.StreamKey` was removed. Still not a *shared* config source between the
   two deployables (deliberately out of scope - same "different deployables, no reference"
   precedent as ever) - the override must still be set on both `ingest` and `api`; documented
   in `.env.example`. New tests: `PipelineStreamKeys_DefaultOptions_MatchFlareIngestPipelineOptionsDefaults`
   (renamed from the old static-method theory) and
   `PipelineStreamKeys_ConfiguredOverride_PropagatesInsteadOfHardcodedDefault` (new, proves
   the actual fix) in `PipelineQueryServiceTests.cs` - full `Flare.Api.Tests` suite green
   (343 tests, 0 failures). This is also still the one place that bridges the two existing,
   slightly mismatched vocabularies: the OTLP-facing `IngestionSignal` enum (v8) calls the
   second signal "Traces"; the pipeline layer calls it "spans"
   (`SpanFlushWorker`, `flare:spans`) - `PipelineStreamKeys`/`FlushHealthKeys` key off the
   former throughout, joining the whole feature on one vocabulary - unchanged by this fix.

- [x] **Ingest-side instrumentation** (`src/Flare.Ingest/Stats/`) - `IFlushHealthTracker`/
      `RedisFlushHealthTracker`/`FlushHealthKeys` (point 2 above), wired into all three
      `*FlushWorker`s' success/failure paths (`RecordSuccessAsync` resets
      `consecutiveErrors` to 0 and updates `lastFlushAt`/`lastBatchSize`;
      `RecordFailureAsync` increments `consecutiveErrors` and sets `lastError`/
      `lastErrorAt` without touching the success fields, so "last time data actually
      reached ClickHouse" stays visible through a run of failures). `ServiceBreakdown`/
      `ServiceAcceptedCounts` (point 3 above) plus the new `IngestionStatsKeys.ServiceRecordsKey`/
      `ServiceBytesKey` helpers, wired into all six OTLP endpoint/service classes
      (HTTP+gRPC × Logs/Traces/Metrics) alongside their existing `RecordAcceptedAsync`
      call. New unit tests: `FlushHealthKeysTests`, `ServiceBreakdownTests`, plus
      additions to the existing `IngestionStatsKeysTests` - 112 tests in
      `Flare.Ingest.Tests` (up from 100), 0 failures.
- [x] **Query API** (`Flare.Api`) - new `GET /api/ingestion/pipeline?minutes=60`, a
      separate endpoint from v8's `/api/ingestion/stats` rather than folded into it (the
      stream/flush sections are an unwindowed live snapshot; only the service breakdown
      uses `minutes`, so one shared window param would only apply to half the payload).
      `PipelineQueryService` (point 1 above): per-signal `XLEN`/`XINFO GROUPS`/`XPENDING`
      reads (the oldest-pending age comes from `XPENDING`'s extended form's own
      `IdleTimeInMilliseconds` on the single oldest entry - the same command
      `ClickHouseFlushWorker.ReclaimStalePendingAsync` already uses for reclaim, read here
      instead of acted on), degrading a stream to `available: false` rather than erroring
      the whole response if it doesn't exist yet (no traffic on that signal since
      Flare.Ingest last started). `Model/PipelineModels.cs`, `Json/PipelineJsonContext.cs`,
      `Query/PipelineStreamKeys.cs`/`FlushHealthKeys.cs` (read-side mirrors, point 4
      above), `Endpoints/PipelineEndpoints.cs`. Pure aggregation logic
      (`BuildFlushHealth`/`BuildServiceBreakdown`) is `internal` and unit-tested directly
      against hand-built `HashEntry[]`/dictionary data, same "test the pure function"
      precedent as `IngestionStatsQueryService`'s own `BuildBuckets`/`BuildTotals` - new
      `PipelineQueryServiceTests` - 183 tests in `Flare.Api.Tests` (up from 171), 0
      failures.
- [x] **Dashboard** (`src/dashboard`) - `lib/pipeline-api.ts` (hand-mirrors the new
      response types, same convention `ingestion-api.ts` documents), `IngestionState`
      extended (not a second polling state - the page renders both queries as one screen,
      and the pipeline endpoint's own stream/flush sections don't use the window param
      anyway, so a second poll loop would buy nothing) to fetch `/api/ingestion/stats` and
      `/api/ingestion/pipeline` together in one `load()` call via `Promise.all`. Three new
      components under a "Pipeline health" heading below v8's existing sections:
      `PipelineStreamsTable.svelte` (per-signal buffered/lag/pending/consumers/oldest-
      pending, lag/pending highlighted via the `warning` badge/text-color variant when
      nonzero), `PipelineFlushHealthTable.svelte` (per-signal last-flush age/batch size,
      consecutive-error count as a `destructive` badge, last error message truncated with
      a `title` tooltip), `PipelineServiceBreakdown.svelte` (one compact table per signal
      with traffic, top-N + "+N more" row, empty-stated if no `service.name` data exists
      yet). `lib/ingestion/format.ts` gained `formatAge`/`secondsSince` (both new -
      formatting a "how long ago" from either a raw seconds value or an ISO timestamp,
      neither of which the existing `formatCount`/`formatBytes` cover).
- [x] **Verified end-to-end, 2026-08-10** - `dotnet test`: 295 tests total (112
      `Flare.Ingest.Tests` + 183 `Flare.Api.Tests`, up from 271 at v9), 0 failures.
      `svelte-check`: 0 errors across 907 files. `npm run build`: clean. Real
      `docker compose up -d --build` (reusing existing populated volumes): confirmed
      `GET /api/ingestion/pipeline` showed real nonzero `length`/`consumers` on all three
      streams before any new traffic. Sent real OTLP logs/traces/metrics via curl with two
      distinct `service.name` values (`checkout-api`, `orders-api`, including one export
      mixing services in one request to exercise the proportional byte-split) - flush
      health (`lastFlushAt`/`lastBatchSize`) and the per-service breakdown both matched
      hand-computed expected values exactly. **Then induced a real backlog**: stopped the
      `clickhouse` container, sent 8 more log exports (24 records) into the now-unflushable
      stream, and confirmed live: `pendingCount` climbed to 24, `oldestPendingAgeSeconds`
      grew in real time, and the flush worker's `lastError`/`consecutiveErrors` recorded
      the real `HttpRequestException: Name or service not known (clickhouse:8123)` failure
      - while `lastFlushAt`/`lastBatchSize` correctly stayed frozen at the prior success,
      per the design in point 2 above. Restarted `clickhouse`; confirmed the backlog did
      *not* recover immediately (`XREADGROUP`'s `>` only delivers new entries - the failed
      batch had already been dropped from the worker's local list, so recovery waits on
      the periodic PEL reclaim once `ReclaimIdle` elapses) and *did* fully recover once
      `oldestPendingAgeSeconds` crossed the 30s `ReclaimIdle` threshold: `pendingCount`
      dropped to 0, a fresh `lastFlushAt`/`lastBatchSize: 24` landed, and
      `consecutiveErrors` reset to 0 while `lastError` correctly stayed visible as history.
      This real run is what surfaced and confirmed the "no immediate retry, recovery is
      reclaim-interval-bound" behavior - not something reasoned about statically. Finally,
      a Playwright-driven Chromium session against the live dashboard confirmed the three
      new tables rendered exactly this data (stream buffers, flush workers with the real
      error message and its red styling, three per-signal service tables) with zero
      browser console errors. Stack torn down after verification (`docker compose down`,
      volumes kept).

### v11 — Auth + multi-user / roles
Promoted out of "Later" and shipped 2026-08-10. Unlike v4/v6/v7/v10 above (each a single
roadmap-listed feature), this item arrived with no sub-bullets or design notes at all —
scoping it was most of the work, done collaboratively before any code: multi-user RBAC on
one shared self-hosted instance (not multi-tenant SaaS isolation - no per-row ownership
anywhere, `alert_rules`/`saved_views` stay exactly as global as they always were), local
username/password for v1 with a documented seam for OIDC/Entra ID/AD later, and - the one
decision that reversed mid-session - embedded SQLite for identity instead of a fourth
Postgres backing-store container, once resource footprint became the deciding factor
(ClickHouse + Redis already run as containers; Seq's own single-binary design was the
reference point for keeping a third one out of the picture). Full writeup, config
reference, and the OIDC pluggability seam: **[docs/auth.md](../docs/auth.md)**.

Shipped as five sequential, independently-buildable/testable PRs, each committed and
verified (full solution build + test suite) before the next started:

- [x] **`Flare.Identity`** (`src/Flare.Identity/`) - new shared project, referenced by
      both `Flare.Api` (full) and `Flare.Ingest` (ingest-key checks only), mirroring the
      existing `Flare.ServiceDefaults` shared-project pattern. `Users`/`Sessions`/
      `IngestApiKeys` SQLite schema (`Migrations/0001_identity.sql`), an idempotent
      `IdentityMigrationRunner` (same shape as `ClickHouseMigrationRunner`), raw
      `Microsoft.Data.Sqlite` (no EF Core, matching how the rest of the repo talks to
      ClickHouse), a custom `SessionAuthenticationHandler` (opaque server-side session
      tokens in an httpOnly cookie, not JWT - revocation needs a server-side row anyway,
      and this is a single-process deployment so JWT's stateless-scaling payoff doesn't
      apply), and `AspNetPasswordHasher` (reuses only `PasswordHasher<T>` from ASP.NET
      Core's shared framework, not the rest of Identity's EF Core/MVC-oriented stack -
      confirmed via a NU1510 package-pruning warning that no separate package reference
      is even needed for it in .NET 10). Also fixed a real NuGet-audit finding along the
      way: pinned `SQLitePCLRaw.bundle_e_sqlite3`/`.core` to the patched `2.1.12`
      (GHSA-2m69-gcr7-jv3q/NU1903), same override pattern already used for
      `Microsoft.OpenApi`. 28 new tests.
- [x] **`Flare.Api` wiring** (`Program.cs`, new `Endpoints/AuthEndpoints.cs`) -
      `AddAuthentication`/`AddAuthorizationBuilder` (`RequireMember`/`RequireAdmin`
      policies), `login`/`logout`/`me`/`bootstrap`/`bootstrap/status`, CORS tightened
      from `AllowAnyOrigin()` to an explicit `Cors:AllowedOrigins` allow-list +
      `AllowCredentials()`. Every existing endpoint group gets `RequireAuthorization()`
      with **zero changes to any of the nine existing endpoint files** - an
      empty-prefix `MapGroup("").RequireAuthorization()` wrapper lets their existing
      `this IEndpointRouteBuilder endpoints => ...; return endpoints;` shape inherit the
      group's policy. The live-tail WebSocket needed no special-casing either: the
      session cookie is sent automatically on the WS upgrade request. 11 new tests
      (fake in-memory `IUserStore`/`ISessionStore`, `IResult.ExecuteAsync` against a
      real `DefaultHttpContext` - no live SQLite/WebApplicationFactory needed).
- [x] **Persistence** (`docker-compose.yml`, `Flare.AppHost/AppHost.cs`) - a shared
      `identity-data` volume/`.data/identity/` path for the SQLite file, `Cors:AllowedOrigins`
      derived from the same `FLARE_DASHBOARD_PORT` the dashboard's own `ORIGIN` already
      uses so the two can't drift apart. Surfaced and fixed a real gap: `IdentityDbConnectionFactory`
      now creates the DB file's parent directory itself (SQLite only ever creates the
      file, not its folder) - would have broken on a genuinely first-ever `aspire run`.
- [x] **Dashboard** (`src/dashboard/`) - `routes/login`, `routes/setup` (first-run admin
      creation), a route guard in `+layout.svelte` that renders a spinner (never the
      real route) while a redirect is pending, so protected content can never flash
      before the bounce completes - safe because `$effect` bodies only run after client
      hydration, never during SSR. New `apiFetch()` wrapper in `lib/api.ts`
      (`credentials: 'include'`) that every other `lib/*-api.ts` module's ~23 raw
      `fetch()` call sites now route through - a mechanical rename, not a rewrite, so no
      endpoint was missed. `svelte-check`: 0 errors/warnings across 914 files.
- [x] **Ingest API keys** (`src/Flare.Ingest/Auth/`, `Aspire.Flare`,
      `Aspire.Hosting.Flare`) - `Admin`-only key management (`Flare.Api`'s new
      `IngestApiKeyEndpoints`), a background-refreshed in-memory cache on the
      `Flare.Ingest` side (never hits SQLite per ingest request), and one deviation from
      the original plan that turned out simpler: **no separate gRPC interceptor** - gRPC-
      on-ASP.NET-Core requests flow through the same middleware pipeline as HTTP (gRPC
      metadata like `authorization` is a plain HTTP header on the wire), so one
      `IngestApiKeyValidationMiddleware` covers both transports. Ships with
      `Auth:IngestKeyRequired=false` so existing anonymous-ingest deployments upgrade
      without breakage. `Aspire.Flare`'s `FlareSettings.ApiKey` and
      `Aspire.Hosting.Flare`'s `AddFlare(..., apiKey: ...)` complete the seam v2's
      `Flare.Aspire` section had already earmarked for this. 11 new tests.

**Verification:** full solution build + `dotnet test` after every one of the five PRs
above (346 tests total, 0 failures by the end); `npm run check`/`npm run build` for the
dashboard PR. Then a real `docker compose up --build` end-to-end pass (curl-driven, same
"verify against the real thing" precedent as v9/v10) - which caught a genuine bug unit
tests couldn't: `Flare.Api`/`Flare.Ingest`'s `Dockerfile`s only `COPY`'d the source
directories that existed *before* this feature, so the new `Flare.Identity`
`ProjectReference` silently resolved to nothing inside the build context (an MSBuild
warning, not an error - `dotnet build` on the host never sees this, only a from-scratch
Docker build with a clean context does). Fixed by adding the same `COPY src/Flare.Identity/`
pair every other cross-project reference already gets. After that fix, the full flow
confirmed live: `bootstrap/status` → 401 on a protected endpoint pre-login → bootstrap
(201, `Set-Cookie`, `httponly`/`secure`/`samesite=lax`) → `/me` and a protected endpoint
both succeed with the cookie → a second bootstrap 409s → create an ingest key (raw key
shown once) → an anonymous OTLP HTTP log export still succeeds (`Auth:IngestKeyRequired`
defaults false) → the log lands in ClickHouse and is queryable through the authenticated
search endpoint (after the normal batch-flush delay, not instant) → logout clears the
cookie and `/me` 401s again → the ingest key revokes cleanly → a CORS preflight from the
dashboard's actual origin (`localhost:3000`) returns the right
`Access-Control-Allow-Origin`/`-Credentials` headers. Stack torn down after
(`docker compose down`), volumes kept - same closing move as v10.

### v12 — Microsoft Entra ID (SSO) auth
Uses the seam v11 deliberately left open (`docs/auth.md`'s former "Pluggability: adding
SSO later" section, now rewritten into a real "Microsoft Entra ID (SSO)" section).
Scoped collaboratively before code, same as v11: single-tenant only (not
`common`/`organizations` — letting any Entra org sign into a self-hosted internal tool
is the wrong default), Entra **App Roles** as the role source (maps 1:1 onto the
existing 3-role enum, no Graph API calls needed), and local username/password stays
enabled alongside it — no deployment is forced to choose.

A real gap surfaced during scoping and got pulled into this item rather than deferred:
`IUserStore.ListAsync`/`SetRoleAsync`/`SetDisabledAsync` existed since v11 but had no
caller — there was no way to manage any user but the single first-run bootstrap Admin
without hand-editing SQLite. Entra auto-provisioning forces this closed (a
newly-provisioned Viewer needs a path to Member/Admin), so the Admin "manage users"
screen shipped in the same pass, not as a follow-up.

- [x] **`Flare.Identity`** — additive migration (`0002_entra_id.sql`:
      `Users.ExternalId`/`AuthProvider` + a unique index), `IUserStore.FindByExternalIdAsync`/
      `CreateFromExternalAsync`, `EntraOptions`. An Entra-provisioned row still gets a
      real, well-formed `PasswordHash` (hashed from a random, never-revealed string, not
      a hand-rolled sentinel) so the local `/login` form simply fails against it like any
      wrong password, with no schema relaxation needed. 40 new tests.
- [x] **`Flare.Api`: OIDC wiring + Entra endpoints** — `AddOpenIdConnect("Entra")` paired
      with a short-lived `AddCookie("EntraExternal")` handoff scheme (the standard
      ASP.NET Core "external login, custom post-processing" pattern — deliberately not
      attempted inside an `OnTicketReceived` event), both only registered when
      `Auth:Entra:Enabled=true`. `GET /api/auth/entra/login` (challenge, `returnUrl`
      validated against `Cors:AllowedOrigins` as an open-redirect guard) and
      `GET /api/auth/entra/complete` (provision-or-look-up by `oid`, mint the same
      `flare_session` cookie password login uses via a `SignInAsync` helper extracted
      from `AuthEndpoints` for both to share). `Microsoft.AspNetCore.Authentication.OpenIdConnect`
      needed its own package reference — confirmed live (`CS0234`) that the OIDC handler
      was split out of the `Microsoft.AspNetCore.App` shared framework back in .NET Core
      3.0 and never moved back, unlike `AddCookie`/the base `AddScheme`.
- [x] **`Flare.Api`: user management** — `GET /api/users`, `PATCH /api/users/{id}/role`,
      `PATCH /api/users/{id}/disabled`, all `Admin`-only, thin wrappers over the
      already-existing `IUserStore` methods above. Both mutating endpoints 400 rather
      than allow demoting/disabling the **last enabled Admin** — an otherwise-unrecoverable
      lockout.
- [x] **Persistence/config** — `docker-compose.yml`'s `api` service and `.env.example`
      gained `Auth__Entra__*`/`ENTRA_*`, same "no working default" pattern as the
      alerting Email channel's SMTP settings. `Flare.AppHost.cs` deliberately untouched —
      Email/SMTP has no AppHost wiring either, for the same reason (no sensible local
      default; set via user secrets if you want to exercise this in local Aspire dev).
- [x] **Dashboard** — `/login` gained a conditional "Sign in with Microsoft" button (only
      when `bootstrap/status` reports `entraEnabled`) via a full-page navigation to
      `entra/login`, plus an `?error=account-disabled` banner for the one failure mode
      that can't flow through a normal form error. New `/users` page (`UserTable.svelte`
      — first real role-gated nav entry in `AppNav.svelte`, first Admin-only route in
      `+layout.svelte`'s guard) using the already-scaffolded `Select`/`Switch`
      components for inline role/enabled editing.
- [x] **Build/test verification, 2026-08-10** — `dotnet build`/`dotnet test` on the full
      solution: 372 tests, 0 failures (up from 346 at v11 — 40 new in `Flare.Identity.Tests`,
      the rest in `Flare.Api.Tests`, split across `SqliteUserStoreTests`,
      `AuthEndpointsTests`, the new `EntraAuthEndpointsTests`, `UserEndpointsTests`).
      `npm run build`/`npm run check` (dashboard): clean, `/users` present in the build
      output.
- [x] **Live-tenant + docker-compose end-to-end verification** — superseded by the v12.1
      follow-up below before it was run: the config-file-based `Auth:Entra:*` values this
      would have verified no longer exist.

**v12.1 follow-up, same day:** the user looked at Seq's own Security settings page (a
screenshot: Authentication provider dropdown, Redirect URI, Authority, Directory/Tenant
ID, Application/Client ID, a write-only client secret field) and asked for the same
experience — each self-hosted Flare operator pastes their own Entra App Registration's
values into an Admin-only dashboard screen, not into `.env`/docker-compose/appsettings.
Scoped via two AskUserQuestion calls before code: **restart required after saving**
(not live hot-reload — simpler and far lower-risk than making ASP.NET Core's OIDC
handler reconfigure itself mid-process, and this is already a restart-cheap
single-replica deployment) and **the database is now the only source of truth** for
`Enabled`/`TenantId`/`ClientId`/`ClientSecret` — the `Auth:Entra:*`/`ENTRA_*` config keys
v12 shipped are removed entirely (`Auth:Entra:DefaultRole` stays config-bound - Seq's
screen doesn't configure a role-mapping fallback either).

- [x] **`Flare.Identity`** — additive migration (`0003_entra_settings.sql`: a
      settings-singleton `EntraSettings` table, `CHECK (Id = 1)`), `EntraSettings`
      record, `IEntraSettingsStore`/`SqliteEntraSettingsStore` (a SQLite
      `INSERT ... ON CONFLICT DO UPDATE ... RETURNING` upsert — `ClientSecret` only
      overwrites when the caller actually supplies one, via `COALESCE(excluded.ClientSecret,
      EntraSettings.ClientSecret)`, so the dashboard's blank-means-unchanged field never
      needs a separate read-before-write). `EntraOptions` trimmed to just `DefaultRole`.
      Client secret stored in plaintext — same trust model as the env var it replaces
      (self-hosted, single-tenant, has to be reversible to send to Microsoft on token
      exchange, unlike `IngestApiKeys.KeyHash`). 5 new tests.
- [x] **`Flare.Api`: restart-required reconfiguration, with zero polling/invalidation
      code** — the `Entra`/`EntraExternal` schemes are now *always* registered (`Enabled`
      is a pure data flag `EntraAuthEndpoints` already checked, not a startup wiring
      decision); a new `EntraOpenIdConnectOptionsConfigurator : IConfigureNamedOptions<OpenIdConnectOptions>`
      applies the database row's Authority/ClientId/ClientSecret. This works because
      ASP.NET Core resolves a named options instance lazily, once, then caches it for the
      process's lifetime unless something explicitly invalidates it — nothing here ever
      does, so a changed row only takes effect on the next resolution, i.e. after a
      restart, for free. New Admin-only `GET`/`PUT /api/settings/entra`
      (`EntraSettingsEndpoints`) — `GET` never returns the real secret (`hasClientSecret`
      boolean instead, matching Seq's own "will not be displayed once set") and computes
      the exact `redirectUri` to register in Entra from the current request's
      scheme+host; `PUT` 400s if `enabled: true` is missing a tenant/client id or has no
      secret on record (this call's or a previous one's).
- [x] **Config cleanup** — `docker-compose.yml`/`.env.example`'s `Auth__Entra__*`/`ENTRA_*`
      lines (added in v12) removed entirely.
- [x] **Dashboard** — new Admin-only `/security` page (`SecuritySettingsForm.svelte`),
      modeled on the Seq screenshot: read-only Redirect URI with a copy button, Tenant
      ID/Client ID/Client secret inputs (secret shows a masked placeholder once set,
      blank stays "unchanged" on save), an Enabled switch, and a post-save "restart
      Flare.Api to apply" notice — no attempt to poll for or detect the restart. Second
      role-gated nav entry alongside `/users`, same Admin-only route-guard pattern.
- [x] **Found and fixed a real bug this live run caught, not unit tests** — the first
      real `docker compose up --build` pass 500'd on *every single request*, including
      `/health`, with `ArgumentNullException: ClientId`. Root cause: making the `Entra`
      OpenIdConnect scheme always-registered (so a disabled/unconfigured deployment could
      still resolve its options once, lazily) ran into a real ASP.NET Core behavior
      neither the plan nor the framework docs called out: `AuthenticationMiddleware`
      resolves (and validates) *every* registered scheme implementing
      `IAuthenticationRequestHandler` - which `OpenIdConnectHandler` does - on *every
      request*, to check whether that request's path matches the scheme's own
      `CallbackPath`, not only on requests that actually challenge it.
      `OpenIdConnectOptions.Validate()` unconditionally requires a non-empty
      `ClientId`/`Authority`, so an all-null `EntraSettings` row broke the whole API, not
      just Entra endpoints. Fixed in `EntraOpenIdConnectOptionsConfigurator` by falling
      back to harmless placeholder values (`ClientId: "not-configured"`,
      `Authority: .../common/v2.0`) when unconfigured - never actually used for anything
      real (`EntraAuthEndpoints` still gates every real use on `Enabled` before
      challenging, and Microsoft's metadata document is only ever fetched lazily on an
      actual auth attempt, not at options-configure time), just enough to satisfy
      `Validate()`. 3 new regression tests
      (`EntraOpenIdConnectOptionsConfiguratorTests`) lock in "never null/empty," though
      the real confirmation was re-running the live stack, not the unit tests - see below.
- [x] **Verified 2026-08-10** — `dotnet build`/`dotnet test` on the full solution: 387
      tests, 0 failures (up from 372 — 5 new in `Flare.Identity.Tests`, 10 new in
      `Flare.Api.Tests`). `npm run build`/`npm run check` (dashboard): clean, `/security`
      present in the build output. Real `docker compose up --build` end-to-end pass
      (after the fix above): fresh volumes, `/health` 200 with Entra unconfigured (the
      regression this session's own bug would have failed), bootstrapped an Admin,
      `GET /api/settings/entra` correctly reported the unconfigured state and a correct
      computed `redirectUri`; `PUT` real-shaped (fake) tenant/client/secret values,
      confirmed `GET` never echoes the secret back; confirmed the `enabled: true` +
      missing-tenant-id 400 guard; confirmed a second `PUT` with `clientSecret: null`
      preserved the previously-saved secret. **Restart-required semantics confirmed live,
      not just by design reasoning**: the login challenge's redirect to Microsoft used
      the stale placeholder `client_id=not-configured` immediately after saving real
      values (options already cached from the very first pre-save request), then after
      `docker compose restart api` the same challenge correctly reached out to
      `https://login.microsoftonline.com/<the-just-saved-tenant-id>/v2.0/.well-known/openid-configuration`
      - failing only because that tenant ID was a fake placeholder GUID, not a real Entra
      tenant, exactly the expected outcome without one. The actual Microsoft
      redirect/App Role mapping through a real browser remains pending - same "needs the
      user's own Entra tenant" caveat v12 already flagged, now against this revised
      implementation instead.

### v13 — Opt-in auth, one consolidated `/auth` page, and Active Directory (LDAP)

Started as "add Active Directory as a third auth provider" and got substantially
reframed by the user before any code: *"dashboard UX is expected better than this, by
default there shouldn't be any auth. users directly see logs page, then in auth page
there should be a button to enable authentication so when clicked we show different
methods of auth (user/pass-entra-active directory) and user chose and configure theirs,
we don't need multiple page such as auth-security. all in only one page."* This reverses
a real, already-shipped v11 assumption (a fresh instance always forced `/setup` first) —
a genuine architecture change, not an incremental addition — so it shipped as three
independently-verifiable passes: the opt-in-auth foundation, page consolidation, then AD
itself landing on top of both.

Two product calls made per the user's explicit steer (*"just think which one makes more
sense not which one is less work to do!"*), not the path of least implementation effort:
**methods coexist, not exclusive** (Local/Entra/AD can all be enabled at once, same
reasoning as v12's Local+Entra coexistence — an exclusive single-method design risks a
real lockout if a group/App-Role mapping is misconfigured, with no escape hatch), and
**Local becomes an explicit toggle too** (`LocalEnabled`), not implicitly always-on, so
all three methods are configured the symmetrical way.

- [x] **The opt-in-auth foundation** — new `AuthSettings` table (migration
      `0004_auth_settings.sql`), settings-singleton shape like `EntraSettings`
      (`Enabled`, `LocalEnabled`, `UpdatedAt`). **The one real backward-compat risk,
      handled at migration time, not via a flat column default:** `Enabled` is seeded as
      `EXISTS(SELECT 1 FROM Users)` — a genuinely fresh install (no Users yet) opens with
      auth off; any v11/v12-shaped database upgrading (Users already present) stays
      `Enabled = true`, exactly as protected as it already was. Dedicated test
      (`Migration_SeedsEnabledTrue_WhenAUserAlreadyExisted_SimulatingAnUpgrade`) locks
      this in. Enforcement is one choke point:
      `ConditionalAuthorizationMiddlewareResultHandler` (`Flare.Api/Auth/`) wraps ASP.NET
      Core's default `IAuthorizationMiddlewareResultHandler` and short-circuits every
      `RequireAuthorization()`/`RequireMember`/`RequireAdmin` check app-wide when
      `Enabled` is false — zero changes needed to any of the nine existing endpoint route
      groups, the same "wrap once, nothing downstream changes" property
      `MapGroup("").RequireAuthorization()` itself established in v11.
      `AuthEndpoints.HandleLoginAsync`/`HandleBootstrapAsync` gained a `LocalEnabled`
      gate (404 when off, same convention Entra/LDAP endpoints already used).
- [x] **Page consolidation** — `/security` and `/users` removed outright (no redirect
      needed — pre-alpha, no external users to break); `/auth` replaces both. Top:
      umbrella "Require sign-in" + `Local username/password` switches
      (`AuthToggleCard.svelte`, with a client-side lockout guard mirroring the
      server-side "can't disable the last enabled Admin" rule). Below: the Entra section
      (moved from the old `SecuritySettingsForm.svelte`, renamed `EntraSecurityForm.svelte`)
      and the Users table (moved from the old `/users` route), both unconditionally
      reachable from the one page. `+layout.svelte`'s route guard skips the login/setup
      redirect entirely when auth is off — Logs renders immediately, no bounce. `AppNav`
      shows a plain "Auth is off" link to `/auth` (visible to everyone — there's no role
      concept to gate it behind while auth itself is off) instead of the user
      badge/logout button in that state.
- [x] **Active Directory (LDAP)** — LDAP/LDAPS bind from Flare's own login form
      (not Windows Integrated Auth/Kerberos — would need the container domain-joined),
      **service-account search-then-bind**: bind as a configured service account, search
      `BaseDn` with `UserSearchFilter` (username escaped per RFC 4515 via new
      `LdapFilterEncoder`, the LDAP-injection equivalent of this repo's parameterized
      SQL/ClickHouse queries elsewhere) to find the real DN, then re-bind as that DN with
      the submitted password to actually verify it — robust against real AD OU
      structures, unlike a fragile direct-DN template. New `LdapSettings` table
      (migration `0006_ldap_settings.sql`, mirrors `EntraSettings`'s shape: Host, Port
      default 636, UseSsl default true, BaseDn, BindDn, BindPassword,
      UserSearchFilter default `(&(objectClass=user)(sAMAccountName={0}))`,
      UniqueIdAttribute default `objectGUID`, three group DNs + DefaultRole for AD's
      native group-membership role source — the App Roles equivalent). `POST
      /api/auth/ldap/login` (`LdapAuthEndpoints`): connection/bind failure → `502`
      (distinct from a wrong-password `401`, so a broken Flare-side config isn't mistaken
      for "everyone's password is wrong"); unknown user or wrong password → generic
      `401` (same anti-enumeration stance as local login); success → reads
      `UniqueIdAttribute` (handles both AD's binary `objectGUID` and OpenLDAP-style
      string `entryUUID`) as `ExternalId`, resolves role from `memberOf` against the
      three group DNs (Admin > Member > Viewer, direct membership only — nested groups
      not resolved, `LDAP_MATCHING_RULE_IN_CHAIN` not implemented) or falls back to
      `DefaultRole`, provisions via the same `IUserStore.FindByExternalIdAsync`/
      `CreateFromExternalAsync` Entra already uses (`AuthProvider: "ActiveDirectory"` is
      just a third provider-agnostic string, zero interface changes). No restart
      required, unlike Entra — LDAP registers no ASP.NET Core auth scheme, settings are
      read fresh from SQLite per login attempt. Package:
      `System.DirectoryServices.Protocols` 10.0.10. Dashboard: login page gained a
      segmented Local/Active Directory toggle (one form, submits to whichever endpoint,
      shown only when both are enabled) via new `loginLdap()`; `/auth` gained an Active
      Directory section (`LdapSecurityForm.svelte`) with an Advanced/collapsed subsection
      for the two AD-default overrides.
- [x] **A real SQLite migration wrinkle, found and fixed via a live test, not
      assumption** — `Users.AuthProvider`'s `CHECK (AuthProvider IN ('Local', 'Entra'))`
      needed broadening to include `'ActiveDirectory'`; SQLite has no
      `ALTER TABLE ... ALTER CHECK`, so `0005_ldap_id.sql` uses the documented
      table-rebuild procedure (`CREATE Users_new` with the wider CHECK → `INSERT ...
      SELECT` copy → `DROP TABLE Users` → `RENAME`). First attempt failed with `FOREIGN
      KEY constraint failed` on the `DROP` — `Microsoft.Data.Sqlite` enables `PRAGMA
      foreign_keys = ON` by default (the *opposite* of raw SQLite's own default), already
      documented elsewhere in this codebase (`SqliteSessionStoreTests.cs`) but wrongly
      assumed not to apply here. Fixed: `PRAGMA foreign_keys = OFF` issued *outside* any
      transaction (a no-op inside one, per SQLite's own docs) before an explicit
      `BEGIN TRANSACTION`/`COMMIT` wrapping the rebuild, `PRAGMA foreign_keys = ON`
      after. New regression test
      (`ApplyAsync_UsersTableRebuild_PreservesExistingRowsAndTheirSessions`) locks in
      that pre-existing Local/Entra rows and their Sessions survive the rebuild intact.
- [x] **A real cross-process migration race, found live via docker-compose, not unit
      tests** — `flarenet-api-1` crashed with `SQLite Error 19: UNIQUE constraint failed:
      schema_migrations.Name` because `Flare.Ingest` and `Flare.Api` both call
      `IdentityMigrationRunner.ApplyAsync` at startup against the same SQLite file and
      raced to record migration 0004's bookkeeping row. Fixed: `INSERT INTO
      schema_migrations` → `INSERT OR IGNORE INTO schema_migrations`. Confirmed via a
      re-run of the same `docker compose up` (all containers came up healthy where
      they'd crashed before) — an honest code comment explains why this specific race
      isn't unit-tested (same-process `Task.WhenAll` against SQLite's own file-level
      locking doesn't reproduce it reliably), matching this repo's established
      "some things are left to e2e only" convention.
- [x] **A real missing native dependency, found live via actual LDAP logins against a
      real directory, not assumption** — every LDAP login attempt (valid or invalid
      credentials alike) 500'd with `TypeInitializationException` →
      `DllNotFoundException: libldap.so.2`. Root cause: `System.DirectoryServices.Protocols`
      on Linux is a P/Invoke wrapper over the OS's own native OpenLDAP client library,
      not a managed implementation, and `mcr.microsoft.com/dotnet/aspnet:10.0` (Ubuntu
      24.04 "Noble," confirmed via `/etc/os-release` inside the running container) 
      doesn't ship it. Fixed: `src/Flare.Api/Dockerfile`'s final stage now runs
      `apt-get install -y --no-install-recommends libldap2` before the entrypoint
      (`libldap2` — Ubuntu's package name; Debian's equivalent `libldap-2.5-0` doesn't
      exist on this base image). Confirmed by installing live into the running
      container first (established the fix before touching the Dockerfile), then
      rebuilding the image and re-running the full login sequence.
- [x] **Live end-to-end verification, 2026-08-14** — `dotnet test` on the full solution:
      426 tests, 0 failures. `npm run check`/`npm run build` (dashboard): clean. Real
      `docker compose up --build`, plus a throwaway `osixia/openldap:1.5.0` container
      (`flare-verify-openldap`, attached to the compose network) seeded with an OU
      structure, two users (`alice` in an admin group, `bob` in none), and the
      `memberof` overlay manually enabled (`ldapmodify -Y EXTERNAL` against `cn=config`
      — not on by default in OpenLDAP, unlike real AD where it's automatic) — confirmed
      via direct `ldapsearch` before ever exercising Flare's own code, isolating the
      libldap bug above to Flare's runtime, not the test fixture. Login sequence against
      Flare's real `/api/auth/ldap/login`: unknown user → `401`, wrong password → `401`,
      LDAP server stopped mid-attempt → `502` (confirmed distinct from the `401`s
      above), `alice` → `200` + `Admin` role from group membership, `bob` → `200` +
      `Viewer` (DefaultRole) — both provisioned with `AuthProvider: "ActiveDirectory"` in
      `GET /api/users`, a second `alice` login reused the same row without re-deriving
      role. Dashboard verified through a real Playwright browser: `/auth` page correctly
      rendered the saved LDAP config and the Users table (admin/alice/bob, correct
      providers/roles); flipping "Require sign-in" on live-redirected to `/login`, which
      showed the segmented Local/Active Directory toggle; signing in as `alice` through
      the AD option landed on the Logs page with the nav badge correctly showing
      "alice / Admin." Verification containers and volumes torn down after
      (`docker rm -f flare-verify-openldap`, `docker compose down -v`).

### v14 — Generic OpenID Connect auth (2026-08-14)

Fourth sign-in method alongside Local/Entra ID/Active Directory, scoped from a screenshot
of Seq's own "Authentication provider: OpenID Connect" Security screen (Authority/Client
id/Client secret/Scopes, computed Callback URL). Architecturally a near-freebie: Entra ID
is already just a named `AddOpenIdConnect()` scheme with a hardcoded Microsoft authority
pattern (`EntraOpenIdConnectOptionsConfigurator`) and `IUserStore.FindByExternalIdAsync`/
`CreateFromExternalAsync` are already provider-agnostic — `SessionAuthenticationHandler`'s
own remarks explicitly anticipated "a future OIDC/Entra ID scheme... as a second,
independent `AddOpenIdConnect()` registration... nothing here needs to change for that."

Two scope calls made explicitly with the user before implementation (`AskUserQuestion`,
not assumed): **v1 is sign-in only** — no end-session/logout propagated to the provider,
matching Entra ID's current behavior exactly, rather than chasing full Seq parity
(End-session redirect URL etc.) — and **role provisioning uses a configurable role-claim
name** (default `roles`) falling back to a **Default role**, both DB-bound like LDAP's
`DefaultRole` rather than Entra's still config-bound one, since arbitrary providers vary
in what claim (if any) carries roles, unlike Entra's fixed `roles` App Role claim.

- [x] **New `OidcSettings` table** (migration `0007_oidc_settings.sql`, settings-singleton
      shape like `EntraSettings`/`LdapSettings`): `Enabled`, `DisplayName` (drives the
      login button's "Sign in with {DisplayName}" label — a generic provider has no fixed
      brand the way Entra's "Microsoft" does), `Authority`, `ClientId`, `ClientSecret`,
      `Scopes` (default `openid profile email`), `RoleClaimName` (default `roles`),
      `DefaultRole`, `UpdatedAt`. `IOidcSettingsStore`/`SqliteOidcSettingsStore` mirror the
      Entra pair's "blank secret means unchanged" upsert convention exactly.
- [x] **`Users.AuthProvider` CHECK widened again** (migration `0008_oidc_id.sql`, the same
      table-rebuild procedure `0005_ldap_id.sql` used for `'ActiveDirectory'`) to add
      `'Oidc'` — SQLite still has no `ALTER TABLE ... ALTER CHECK`.
- [x] **A third `AddOpenIdConnect()` scheme** (`OidcAuthenticationDefaults`: scheme
      `"Oidc"`, paired external cookie `"OidcExternal"`) registered alongside Entra's in
      `Program.cs`, with its own `OidcOpenIdConnectOptionsConfigurator` applying `Authority`
      directly (no tenant-id interpolation, unlike Entra) plus a `Scope` collection rebuilt
      from the stored space-separated string. **Needed an explicit distinct `CallbackPath`**
      (`/signin-oidc-generic`) — `OpenIdConnectOptions` defaults every scheme to
      `/signin-oidc`, which would otherwise collide with Entra's own registration in the
      same app; not something the Entra-only codebase had to think about before. Same
      harmless-placeholder-when-unconfigured trick and same restart-required semantics as
      Entra's configurator, for the same reasons.
- [x] **`OidcAuthEndpoints`** (`GET /api/auth/oidc/login`, `GET /api/auth/oidc/complete`)
      mirrors `EntraAuthEndpoints` almost exactly, reusing its `ValidateReturnUrl` helper
      directly rather than duplicating it. Two deliberate divergences: external id reads
      the standard, provider-portable `sub` claim (vs Entra's Microsoft-specific `oid`
      preference), and role resolution reads `settings.RoleClaimName` dynamically instead
      of a hardcoded `"roles"` literal. `AuthProvider: "Oidc"` is just a fourth
      provider-agnostic string — zero `IUserStore` interface changes needed.
- [x] **`OidcSettingsEndpoints`** (`GET`/`PUT /api/settings/oidc`, Admin-only) mirrors
      `EntraSettingsEndpoints`'s validation (`Enabled=true` requires Authority+ClientId+a
      client secret on record) plus its own check that `Scopes`/`RoleClaimName` aren't
      blank. `AuthSettingsEndpoints`' "at least one method enabled" lockout guard and
      `AuthEndpoints.HandleBootstrapStatusAsync`'s response (`oidcEnabled`,
      `oidcDisplayName`) both extended the same way LDAP's addition extended them in v13.
- [x] **Dashboard**: the same 3-file-per-method convention (`oidc-settings-api.ts` →
      `oidc-settings/state.svelte.ts` → `oidc-settings/context.ts`) plus
      `OidcSecurityForm.svelte` (Display name/Authority/Client ID/Client secret/Scopes/Role
      claim name/Default role/Enabled, read-only Callback URL with copy button) added as a
      third card in `/auth`'s `xl:grid-cols-2` grid — the page's own pre-existing comment
      had already flagged that exact spot for "a future generic OpenID Connect section."
      `AuthToggleCard`'s lockout guard and `/login`'s button row both extended to treat
      OIDC as a peer of Entra (`startOidcLogin()`, a generic "Sign in with {displayName}"
      button, provider-neutral disabled-account error copy since the query param alone
      doesn't say which SSO provider triggered it).
- [x] **Verification performed**: `dotnet build`/`dotnet test` on `Flare.Api`,
      `Flare.Api.Tests` (278 passed), `Flare.Identity.Tests` (52 passed), and `Flare.Ingest`
      (unaffected build) — new `FakeOidcSettingsStore`, `OidcSettingsEndpointsTests`,
      `OidcAuthEndpointsTests`, `OidcOpenIdConnectOptionsConfiguratorTests`,
      `SqliteOidcSettingsStoreTests` mirror the Entra suite 1:1. Dashboard: `npm run check`
      (0 errors) and `npm run build` (clean, `auth/_page.svelte.js` compiled). **Not yet
      done**: a live end-to-end run against a real OIDC provider and a real `docker compose`
      restart, the way v13's LDAP work was verified against a throwaway OpenLDAP container —
      left for whenever this actually gets exercised against a real Okta/Auth0/Keycloak
      tenant.

### v15 — Reverse-proxy (trusted header) auth (2026-08-14)

Fifth sign-in method, requested as "another auth method people use" and picked as "the
lightest-weight option" from a menu the user was offered (SAML, reverse-proxy header
trust, social/OAuth login, passkeys/WebAuthn, Kerberos/Windows Integrated, mTLS) —
trusts an identity header an already-authenticating reverse proxy (Authelia, Authentik,
oauth2-proxy, Cloudflare Access, Tailscale Serve, ...) sets, instead of Flare talking to
an IdP itself. Unlike OIDC (which reused Entra's proven `AddOpenIdConnect()` shape),
this was genuinely new ground for the codebase - confirmed via search before designing
anything: no `UseForwardedHeaders`, no reverse-proxy config, no IP-allowlist concept
existed anywhere in `Flare.Api`/`docs/`.

Two scope calls made explicitly with the user before implementation (`AskUserQuestion`):
**dashboard-triggered, not ambient** — a new `POST /api/auth/proxy/login` endpoint the
`/login` page calls automatically, mirroring how every other method converges on
`AuthEndpoints.SignInAsync`, rather than a new piece of request-pipeline middleware that
self-authenticates every request (closer to how Grafana's `auth.proxy`/Authelia
forward-auth actually work, but a bigger architectural insertion this codebase didn't
have a precedent for) — and **role mapping via an optional groups header** matched
against three configurable group names, mirroring LDAP's three-group-DN pattern instead
of Entra's fixed claim or a Default-role-only design.

- [x] **A trusted-network allowlist is mandatory, not optional (fail-closed)** — the
      single most important design decision, stated repeatedly in both code comments and
      docs, not just decided once and forgotten: a header can be trivially spoofed by any
      client reaching `Flare.Api` directly, so enabling this method without at least one
      valid CIDR is refused server-side (`400`). New `TrustedProxyNetworks`
      (`Flare.Api/Auth/`) wraps `System.Net.IPNetwork` (.NET 8+ BCL type, no new
      package) to parse/match — checks `HttpContext.Connection.RemoteIpAddress` (the
      request's own direct TCP peer) only, deliberately never `X-Forwarded-For`/
      `UseForwardedHeaders()`, since trusting one spoofable header to establish trust for
      a *different* spoofable header would defeat the entire point. Normalizes an
      IPv4-mapped-IPv6 address (`::ffff:172.18.0.5`, what Kestrel commonly reports for a
      peer behind Docker's default bridge network) before matching — found via reasoning
      through the real Docker networking path, not live-tested this session (flagged as
      the main unverified assumption below), documented in the class's own remarks and
      locked in by a dedicated test.
- [x] **New `ProxyAuthSettings` table** (migration `0009_proxyauth_settings.sql`,
      settings-singleton shape like every other method's): `Enabled`, `HeaderName`
      (default `Remote-User`, Grafana's own convention), `TrustedProxyCidrs` (raw
      newline/comma-separated string), `GroupsHeaderName`/`AdminGroup`/`MemberGroup`/
      `ViewerGroup` (all optional), `DefaultRole`. The one settings record among five
      methods with **no secret field at all** — nothing to mask, so
      `IProxyAuthSettingsStore.SaveAsync` needed none of the other four stores'
      "blank means unchanged" convention.
- [x] **`Users.AuthProvider` CHECK widened a third time** (migration
      `0010_proxyauth_id.sql`, the same table-rebuild procedure `0005_ldap_id.sql`/
      `0008_oidc_id.sql` used) to add `'ReverseProxy'`.
- [x] **`ProxyAuthLoginEndpoints`** (`POST /api/auth/proxy/login`) shaped like
      `LdapAuthEndpoints` (single POST/JSON, no ASP.NET Core scheme, settings read fresh
      per request, no restart needed) rather than Entra/OIDC's redirect dance — there's
      no external provider to redirect to. Three distinct failure codes for
      debuggability (`404` disabled, `403` untrusted network, `401` header missing or
      account disabled), `sub`-claim-style identifier (the header value itself, doubling
      as the seed username), `ResolveRole` mirrors `LdapAuthEndpoints.ResolveRole`'s
      group-matching precedence exactly, just comma-split header values instead of
      `memberOf`.
- [x] **`ProxyAuthSettingsEndpoints`** (`GET`/`PUT /api/settings/proxyauth`) — the one
      method whose "enable" validation exists purely for safety, not usability: rejects
      enabling with a blank header name or zero CIDR entries that actually parse.
      `AuthSettingsEndpoints`' lockout guard and `AuthEndpoints.HandleBootstrapStatusAsync`
      both extended the same mechanical way every prior method extended them.
- [x] **Dashboard**: same 3-file-per-method convention plus `ProxyAuthSecurityForm.svelte`
      (Header name, Trusted proxy CIDRs textarea, Advanced-collapsed groups/role-mapping
      fields, Default role, Enabled — no secret field, "Saved." banner not
      "restart Flare.Api" since this method needs neither) as a fourth card in `/auth`'s
      grid. `/login` gained a `showProxyAuthLoading`-gated auto-login effect
      (`auth.loginViaProxy()`, a new `AuthState` method mirroring `login`/`loginLdap`) —
      the one method with **no button at all**, since there's no user action to trigger;
      a failed attempt falls through to whatever other methods are configured, with the
      error only shown once (suppressed when a fallback form will already display it
      inline, to avoid double-showing the same message).
- [x] **Verification performed**: `dotnet build`/`dotnet test` on `Flare.Api`,
      `Flare.Api.Tests` (303 passed, up from 278 - new `TrustedProxyNetworksTests` got
      particular attention on CIDR edge cases including the IPv4-mapped-IPv6 case),
      `Flare.Identity.Tests` (57 passed, up from 52), `Flare.Ingest` (unaffected build).
      Dashboard: `npm run check` (0 errors) and `npm run build` (clean, both
      `login/_page.svelte.js` and `auth/_page.svelte.js` grew as expected). **Not yet
      done, same gap OIDC's v14 entry flagged for itself**: a live end-to-end run behind
      a real reverse proxy (throwaway nginx/Authelia/oauth2-proxy container) confirming
      the trusted-CIDR boundary actually rejects a direct, proxy-bypassing request in
      practice, not just in the unit-tested `TrustedProxyNetworks` logic — left for
      whenever this gets exercised against a real deployment.

### v16 — Log pattern detection (Drain clustering) (2026-08-17)

Not a prior "Later" item — proposed fresh ("This could become another killer feature",
inspired by OpenObserve's log-pattern-statistics feature): cluster similar log message
bodies (`"GET /api/orders/123"`/`"GET /api/orders/456"` → `"GET /api/orders/<*>"`) and
surface them ranked by occurrence count in a new Patterns view.

Two implementation shapes were discussed before scoping: computing clusters at query
time (cheap to build, no schema change) vs. computing them once at ingest time and
storing a `PatternId` per row (real `GROUP BY` aggregates over arbitrary time ranges, at
the cost of a schema/pipeline change). Query-time was rejected on a direct efficiency
comparison, not a "less work" one — the flagship stat ("12,481 occurrences" over a wide
window) needs either scanning `Body` text for every matching row on every page load, or
silently capping/sampling and breaking the promised exact count; ingest-time pays the
clustering cost once, in the flush worker that's already CPU-bound work happening
anyway, turning the read side into a plain `GROUP BY PatternId` — the same shape the
existing volume histogram/service-breakdown aggregates already use.

Scoped down from the original pitch in one place, confirmed with the user before
implementation (`AskUserQuestion`): **no duration/p95 in v1** — logs have no duration
field anywhere in the schema (only `spans.DurationNano` does), and nothing in the
codebase joins `logs`↔`spans` for aggregates. The pattern card ships with occurrence
count, error count, first/last seen; duration is a named Later item requiring a
`TraceId`/`SpanId` join, and would only cover logs that carry trace context anyway.

- [x] **Drain matcher** (`Flare.Ingest/Patterns/`) - a simplified Drain (logpai/Drain3-
      style) log-template miner: `DrainPatternMatcher` masks UUID/hex/numeric substrings
      to `<*>` before whitespace tokenization, buckets by `(tokenCount, firstToken)`,
      matches the best candidate cluster above `LogPatternOptions.SimilarityThreshold`
      (generalizing differing positions to `<*>`) or creates a new cluster. In-memory
      only, no persistence across a restart or across replicas - unlike
      `LogEventPipelineOptions.ConsumerName` (now per-process, see "Multi-node scaling"
      below), this one isn't fixed by per-replica identity: independent replicas would
      each build their own clusters from whatever logs they happen to see, fragmenting
      `PatternId`s for the same template. A real fix needs shared cluster state; not
      attempted as part of that item (**fixed 2026-08-23** - see "Multi-node scaling"
      below's follow-up: cluster storage moved behind `IPatternClusterStore`, with an
      opt-in Redis-backed shared store). `PatternId` is a deterministic SHA-256-derived hash of the
      finalized template text (not sequential), so the same template re-emerging after a
      restart gets the same id - softens, doesn't eliminate, the restart-reset
      limitation. A global `MaxTemplates` LRU cap (default 10,000) bounds worst-case
      memory growth from adversarial/high-cardinality bodies, same safety-cap instinct as
      `SafetyOptions()`/`StreamMaxLength` elsewhere.
- [x] **Computed at flush time, not OTLP-receipt time** - `LogPatternAnnotator` runs
      inside `ClickHouseFlushWorker.FlushAsync`, right before the batch write, not in
      `OtlpLogMapper`/the OTLP gRPC/HTTP endpoints - keeps ingestion request latency
      untouched, the same reasoning that motivated the Redis-Streams buffer in the first
      place. `LogPatternOptions.Enabled` (default `true`) is an instant, config-only
      rollback valve.
- [x] **Migration `0010_logs_pattern.sql`** - `PatternId`/`PatternTemplate`
      (`LowCardinality(String) DEFAULT ''`) appended to `logs` via `ALTER TABLE ... ADD
      COLUMN IF NOT EXISTS`, same convention `0002_logs_event_id.sql` used for `EventId`.
      No skip index in v1 (a `GROUP BY` doesn't benefit from one; the one path that
      could, drilling into a single pattern's rows, already inherits
      `LogFilterSqlBuilder.DefaultLookback`'s time bound) and no backfill of historical
      rows (same precedent as `EventId`'s own migration) - unbackfilled rows read back as
      `PatternId=''` and are simply excluded from the ranked list (`LogPatternQueryBuilder`
      filters `PatternId != ''`), not shown as a misleading "unknown" bucket.
- [x] **Query API**: new `POST /api/logs/patterns` (`LogsEndpoints.cs`,
      `LogPatternQueryBuilder.cs`, `LogQueryService.GetPatternsAsync`) mirrors
      `/api/logs/aggregate`'s shape exactly - `GROUP BY PatternId`, `countIf(SeverityNumber
      >= 17)` for the error count (OTel's ERROR floor), `ORDER BY Count DESC LIMIT
      {topN}` (clamped 1-1,000, default 200), same `SafetyOptions()`/`LogFilterSqlBuilder`
      reuse as every other log query. `LogFilter` gained a `PatternId` equality field
      (mirrors the existing `TraceId`/`SpanId` shape) for the drill-down below.
- [x] **Dashboard**: shipped first as a standalone top-level `/patterns` route, then
      revised same-day per direct user feedback ("wasn't expecting a new page for it")
      into a modal opened from an icon-button trigger next to the Logs page's search box
      (`PatternsModal.svelte`, `RegexIcon` trigger inside `LogsToolbar`) - patterns are
      always "patterns within what I'm currently looking at," so a page switch away from
      the Logs Explorer's own filter context was the wrong shape for this. The modal
      reads `LogsExplorerState.buildFilter`/a new public `currentRange()` wrapper
      directly (the window the log table is *currently* searching, respecting a
      VolumeChart bucket-click selection) rather than owning a separate filter toolbar
      of its own - one less state class, one less context, no filter UI duplicated
      against what's already visible on the page underneath. **Drill-down** ("View
      occurrences", renamed from "View examples") closes the modal and calls
      `LogsExplorerState.applyPatternIdFilter` directly - no URL round-trip needed once
      the modal and the Logs Explorer share one page/state instance, unlike the
      discarded route version's `/?patternId=<id>&patternTemplate=<text>` link - still
      surfaced as a dismissible badge in `LogsToolbar` since it's a sticky filter with no
      other UI control to clear it otherwise. The route version's `TimeRangePicker.svelte`
      prop-driven refactor (done so a separate Patterns toolbar could reuse it) was
      reverted along with it - once nothing but `LogsToolbar` renders `TimeRangePicker`
      again, the props were unearned complexity, not earned reuse.
- [x] **Verification performed**: `dotnet test` on `Flare.Ingest.Tests` (140 passed) and
      `Flare.Api.Tests` (339 passed) - new `DrainPatternMatcherTests` (tokenization/
      wildcarding including the "pure a-f letter word isn't hex" case, threshold merge/
      split, LRU eviction, determinism-across-restarts-of-the-same-template),
      `LogPatternAnnotatorTests` (hand-written fake matcher, no mocking framework, same
      convention as `FakeClickHouseLogEventWriter`), `LogPatternQueryBuilderTests`
      (mirrors `LogAggregateQueryBuilderTests`'s exact-SQL-text-assertion style). Dashboard:
      `npm run check` (0 errors) and `npm run build` (clean, `patterns/_page.svelte.js`
      compiled). ~~**Not yet done**: a live end-to-end run against a real stack...~~ **Live
      e2e-verified 2026-08-22.** Fresh `docker compose up --build`, real OTLP traffic
      via a standalone `ExampleApp.LogGenerator` run (`ConnectionStrings__flare=
      http://localhost:4317`, no Aspire needed). Patterns modal correctly collapsed
      parameterized routes into wildcarded templates ("... <*> in <*>" at 5
      occurrences, "exceeding the 500ms threshold" at 5, etc.) with real Count/
      Errors/First seen/Last seen columns; "View occurrences" round-tripped
      correctly - closing the modal and applying `patternId` as a real Logs Explorer
      filter chip, table showing exactly the matching rows (confirmed via a real
      `POST /api/logs/patterns` network request, no stale/cached data). No bugs
      found. (First attempt showed "No patterns yet" against several-hours-old data
      still sitting in the kept volume from earlier sessions - a time-range mismatch
      against the modal's default window, not a detection bug; fresh traffic
      resolved it immediately.)

### v17 — Trace waterfall polish: continuous demo traces, critical-path highlighting, span counts, Service Map (2026-08-18)

Not a prior "Later" item, same shape as v16 - a same-day chain of feedback-driven passes
against the already-shipped v4 Traces feature, each landed and merged before the next
began. Started from a bug report ("Traces page is always empty in ExampleApp") and grew
into four follow-ups once the underlying data existed to react to.

- [x] **Bug: the Traces page had nothing to show** - `RandomLogGeneratorWorker`'s
      background trickle loop (v4's own e2e verification only exercised
      `GenerateBurst`/`POST /generate-burst`) never started an `Activity`, so nothing
      exported unless a user manually hit that endpoint. Fixed by wrapping every
      trickled `SampleLogEvents.EmitOne()` call in its own span, same `ActivitySource`
      the burst path already used.
- [x] **Flat single-span traces → a real waterfall** - immediate follow-up once traces
      existed at all: both paths emitted one flat span per event, so the waterfall view
      had nothing to render beyond a single row. `EmitWaterfall()` (renamed from the
      one-off trickle fix, now shared by both the trickle loop and `GenerateBurst`)
      builds a small `handle-request` → `auth-check` → `render-response` chain instead,
      with backdated timestamps (`Activity.SetEndTime`, not real `Task.Delay`s - no
      reason to block the loop, or a 500-item burst, for fake latency this is dummy data
      is showing off anyway).
- [x] **Critical-path highlighting** (`lib/traces/critical-path.ts`) - external review
      feedback that span-count-agnostic waterfalls miss the real story ("a trace taking
      1.14s but only 700ms actually determines the response"). Confirmed the idea with
      the user before building (`AskUserQuestion`) since it needs genuine concurrency to
      demonstrate anything - a straight sequential chain is trivially "100% critical
      path." Two pieces, both required: (1) `computeCriticalPath` - a standard trace-CPA
      decomposition (walk each span's children latest-end-first; the gap between the
      cursor and the next-latest child's end is the parent's own self time), attributing
      every moment of the root's duration to exactly one span; (2)
      `EmitWaterfall` reshaped to actually fork - `inventory.query` (with a nested
      `sql.query`) racing `payment.request` - so there's a bottleneck branch and a
      loses-the-race branch to distinguish. `TraceWaterfall.svelte` renders a sticky
      "Critical path · N of M spans · top contributor" callout plus per-bar
      ring/fade treatment.
- [x] **Trace list: per-trace span count column** - external review feedback again
      ("a 200ms trace with 2 spans is very different from one with 80"). Real backend
      work, not a dashboard-only add (confirmed the scope with the user first): new
      `SpanDto.SpanCount` (nullable, the one field that isn't a 1:1 mirror of
      `0007_spans.sql` - documented as the deliberate exception), populated only for
      `SpanFilter.RootSpansOnly` searches via a new `SpanCountQueryBuilder` - one
      `GROUP BY TraceId` follow-up query over the returned page's trace ids, not a
      correlated subquery per row (cheap because `TraceId` leads `spans`' `ORDER BY` -
      `WHERE TraceId IN (...)` is a primary-key-prefix lookup). Kept the trace list's
      "Name" column label rather than the feedback's suggested "Operation" rename -
      consistent with `SpanDto.name` and the waterfall's own header, confirmed with the
      user rather than assumed.
- [x] **Service Map tab** (`lib/traces/service-map.ts`, `ServiceMap.svelte`,
      `ServiceMapNode.svelte`) - external review feedback once more ("the trace becomes
      a journey through your architecture, not just a list of spans"), reusing
      `@xyflow/svelte` + `@dagrejs/dagre`'s `layoutGraph` wholesale from the Resources
      page's own `ResourceGraph.svelte` rather than rebuilding graph infra - same
      dark-`colorMode` requirement, same card/`Handle`-on-both-sides node shape. Flagged
      a real scoping snag before building (confirmed with the user, `AskUserQuestion`):
      `ServiceName` is a per-*process* OTel Resource attribute, not per-span, so a
      single-process trace (this whole example app) can't naturally produce a
      multi-service-looking map. Resolved via the standard `peer.service` span
      attribute (spec-correct, not a hack) - `EmitWaterfall`'s `inventory.query`/
      `sql.query`/`payment.request` spans now carry it (`inventory-service`/`postgres`/
      `payment-service`), and `buildServiceMap` treats a span's `peer.service` as
      overriding its own `serviceName` when present, so real multi-service deployments
      and this fabricated single-process one both render correctly through the same
      code path. New `Waterfall | Service Map` toggle in the trace-detail page header
      (plain buttons, `buttonVariants`, same active/inactive pattern `AppNav` uses for
      its nav links - no `Tabs` primitive exists in `ui/` yet, so this didn't add one for
      a single two-way toggle).
- [x] **Verification performed**: each of the five passes built/tested independently
      before the next began (`dotnet build`/`dotnet test` per backend change, ending at
      342 passed on `Flare.Api.Tests`; `svelte-check` 0 errors across 1,126 files and
      `npm run build` clean after every dashboard change). The trickle/waterfall/fork
      changes were confirmed against a **live running AppHost**, not just unit tests -
      pulled real OTLP traces straight from the Aspire dashboard's own collector
      mid-session and verified the exact shape (unique trace ids, correct parent/child
      links, the fork structure) before trusting it. ~~**Not yet done**: a live visual check...~~ **Live e2e-verified 2026-08-22**
      against a real multi-span trace (`ExampleApp.LogGenerator`'s own
      `handle-request` → `auth-check`/`inventory.query`/`sql.query`/`payment.request`
      chain, 6 spans). Waterfall tab: "Slowest spans" ranked correctly, critical path
      correctly highlighted (orange borders on `inventory.query`/`sql.query`) with
      the callout reading "Critical path · 4 of 6 spans · sql.query accounts for 68%
      of the trace (264.0ms)" - a real, non-trivial percentage computed from real
      span durations, not a placeholder. Service Map tab: real graph rendered
      (`unknown_service:Exam...` / `payment-service` / `inventory-service` /
      `postgres` nodes, correct per-edge call counts and durations). No bugs found.

### v18 — Active Directory (LDAP): TLS certificate pinning + chain/bundle support (2026-08-19)

Two same-day, directly-related passes; recorded together since the second is a
straight-up fix to the first, not independent work - `docs/auth.md`'s "Known
limitations" bullet under v13's LDAP work (never itself given a proper header here, a
doc gap - see that section above) originally read "TLS certificate validation relies on
the container/host's own OS trust store... there's no in-app certificate-pinning UI."

- [x] **PR #90 - certificate pinning itself.** Admins can paste a PEM certificate into a
      new **Pinned server certificate** field on the AD settings form (under "Use LDAPS
      (TLS)"); when set, `LdapAuthEndpoints.CreateConnection` wires
      `SessionOptions.VerifyServerCertificate` to a new `LdapCertificateTrust.Validate`
      helper that builds an `X509Chain` with `TrustMode = CustomRootTrust` and only that
      one certificate in `CustomTrustStore` - the OS/container trust store is bypassed
      entirely for that connection. Fails closed on an expired pinned certificate
      (pinning isn't an "ignore expiry" escape hatch). `LdapSettings.PinnedCertificatePem`
      stores it as plaintext with direct-clear (not `BindPassword`'s COALESCE-preserve)
      semantics, since a certificate isn't a secret - additive migration
      `0011_ldap_pinned_certificate.sql`, no `hasX` redaction on the DTO. Save-time PEM
      validation in `LdapSettingsEndpoints` (400 on malformed input).
- [x] **Follow-up, same day: bundle/chain support** (closes a gap identified while
      re-reviewing the PR right after merge, not a bug report from the field) -
      `LdapCertificateTrust.Validate` only ever pinned a single certificate. A real
      two-tier private CA (offline root + issuing intermediate) presents `leaf →
      intermediate`, and .NET's chain builder has no guaranteed way to fetch a missing
      intermediate (AIA fetching can be unavailable in a container with restricted
      egress) - pinning just the root could fail to build a chain even though trust was
      conceptually correct. Fixed without any schema/API shape change -
      `PinnedCertificatePem` stays one nullable string, now read as a PEM **bundle**
      (concatenated blocks, same convention as any CA bundle file - and exactly what
      `openssl s_client -showcerts`, already docs/auth.md's suggested command, prints by
      default). `Validate` now takes an `X509Certificate2Collection`; certificates are
      sorted by a new `LdapCertificateTrust.IsTrustAnchor` (self-signed, i.e. `Subject ==
      Issuer`) into `CustomTrustStore` (roots) vs. `ExtraStore` (intermediates) - handles
      the two-tier-CA case and "pin two DC certificates side by side during a rotation
      window" with the same mechanism. `LdapSettingsEndpoints` save-time validation
      extended to reject (400) a bundle with no self-signed certificate at all - no trust
      anchor, would otherwise fail closed at every login with no clue why. New
      `TestCertificates.CreateRootIntermediateAndLeaf` (3-tier chain, throwaway/hermetic
      like its existing `CreateCaAndLeaf`/`CreateSelfSigned` siblings) backs new
      `LdapCertificateTrustTests`/`LdapSettingsEndpointsTests` cases. `dotnet test
      Flare.Api.Tests` - 357/357 (up from 350). Dashboard copy and `docs/auth.md` updated
      to describe bundle support. **Not yet done**: no live e2e run against a real
      two-tier private CA (self-signed-root-only was the only path v13's own LDAP
      verification exercised against a real directory) - same "left for whenever this
      gets exercised for real" gap v14/v15/v16/v17 each flagged for themselves.

### v19 — Logs: correlate a log event to its enclosing span's duration (2026-08-21)

Picked up the same day it was scoped out (see the "Later" bullet above for the original
trade-off writeup and the two heavier shapes it considered). User explicitly rejected a
lighter single-item-lookup scope in favor of "the right thing... even if it's a huge
change" - staged across a design pass instead of shipped as a shortcut.

Neither of the two shapes originally scoped (query-time `logs ⋈ spans` join across an
arbitrary matched result set; async ClickHouse-mutation backfill once a span closes) got
used. Research surfaced a third shape already precedented in this exact codebase:
`SpanDto.SpanCount` (v17) solves a structurally identical problem - enrich a page of
already-fetched rows with derived data from the other table - via a small, bounded
follow-up query keyed on the *current page's own keys*, not a join over the whole
matched result set. This item mirrors that pattern for "the enclosing span's duration,"
matching exact `(TraceId, SpanId)` pairs instead of `SpanCount`'s single-column
`GROUP BY TraceId`. No schema/migration change (`spans.DurationNano` already existed,
stored since v4) and no mutation.

- [x] **`SpanDurationQueryBuilder`** (`Flare.Api/Query/`) - pure, ClickHouse-free
      `(TraceId, SpanId)` pairs → SQL, same style as `SpanCountQueryBuilder`: `SELECT
      TraceId, SpanId, DurationNano FROM spans WHERE TraceId IN {traceIds:Array(String)}
      AND SpanId IN {spanIds:Array(String)}`. Deliberately two flat `IN` arrays rather
      than a single `Array(Tuple(String,String))` parameter - no existing usage anywhere
      in this codebase confirms `ClickHouse.Driver` 1.3.0 actually supports binding a
      tuple array, and this item didn't need to block on that uncertainty. The looser
      filter can in principle cross-match a spurious pair (trace A's SpanId happening to
      also appear under trace B - astronomically unlikely under OTel's 8-byte random
      SpanId); harmless regardless, since the caller's merge step keys on the exact pair
      and a spurious row is simply never looked up. `SpanDurationQueryBuilderTests`
      (4 cases: SQL shape, separate-array parameter binding, pair dedup, independent
      TraceId/SpanId dedup when values are shared across different pairs).
- [x] **`LogQueryService.WithSpanDurationsAsync`** - opt-in follow-up (new
      `LogSearchRequest.IncludeSpanDuration`, defaults `false`; lives on the request
      alongside `Cursor`/`PageSize`, not on `LogFilter`, since it shapes the response
      rather than which rows match) called from `SearchAsync` right before returning,
      only when the flag is set and the page has ≥1 event with non-empty
      `TraceId`/`SpanId`. Builds the page's distinct pairs, runs
      `SpanDurationQueryBuilder`, merges `DurationNano` back via `e with {
      SpanDurationNano = ... }` - a pair with no match (span not flushed yet, or no span
      at all) stays `null`, not a sentinel. Opt-in specifically because `/api/logs/search`
      is also called by Patterns' drill-down and CSV export, neither of which shows a
      duration column and shouldn't pay for the extra query - same "only the mode that
      needs it pays for the follow-up query" principle `SpanFilter.RootSpansOnly`
      already established for `SpanCount`. `LogEventDto` gained a nullable
      `SpanDurationNano ulong?` - the one deliberate exception to its "field-for-field
      DDL mirror" convention, same shape as `SpanDto.SpanCount`'s own documented
      exception. No `LogsJsonContext`/endpoint changes needed - both already cover these
      record types wholesale. The merge logic itself isn't independently unit tested,
      matching this codebase's existing convention: `SpanQueryService`'s equivalent
      `WithSpanCountsAsync` has no unit test either (no fake `IClickHouseClient` exists
      in `Flare.Api.Tests`) - relies on live e2e for that path, same as the precedent it
      mirrors. `dotnet test Flare.Api.Tests` - 402/402 (up from 398).
- [x] **Dashboard**: `LogsExplorerState`'s two search call sites (`runSearch`,
      `loadMore`) set `includeSpanDuration: true`; `SpanDetailSheet.svelte`'s
      linked-logs fetch and `logs/export.ts` left untouched (still default `false`). New
      "Duration" column in `LogTable.svelte`/`LogRow.svelte` (between Service and
      Message), and `EventDetailSheet.svelte`'s existing Trace ID/Span ID grid gained a
      third "Span duration" cell (2-col grid → 3-col) - both via the existing
      `formatDurationNano` house formatter (`$lib/traces/duration.ts`, already used by
      `SpanDetailSheet`/`TraceWaterfall`), showing `—` when absent. No per-item async
      fetch needed in `EventDetailSheet` - the duration arrives already populated on the
      `LogEventDto` from the bulk follow-up. `npm run check` (0 errors, 0 warnings) and
      `npm run build` (clean).
- [x] **Deliberately deferred, not folded in here**:
      - **Live-tail duration enrichment** - live-tail rows come straight off the Redis
        Stream (`flare:logs`) before ever reaching ClickHouse, so a freshly-arrived
        row's span usually hasn't flushed yet; live-tail rows simply show no duration,
        same as today. Would need the "detect a span just closed" pipeline machinery
        this item's original scoping note already flagged as a separate, real cost.
      - **True cross-query aggregation** - p95 span duration in Patterns, or
        sorting/filtering the Logs table by duration. Both operate over an *entire
        matched range*, not one page, so they'd need one of the two heavier shapes kept
        in the "Later" bullet above (the join or the mutation-backfill), not this
        bounded-follow-up shape - stays a separate Later item.
- [x] **Live e2e run + bug: live mode showed "0" instead of hiding the column.** Run
      against a real stack the same day - paginated/non-live search showed correct real
      duration values, confirming the bounded follow-up query end-to-end. But every
      live-tailed row showed "0", not `—`, for `spanDurationNano` - two compounding
      issues, both in `LogRow.svelte`/`EventDetailSheet.svelte`:
      1. **Null-check bug.** `LogsJsonContext` has no `DefaultIgnoreCondition`
         configured, so an unset `LogEventDto.SpanDurationNano` (`ulong?`) serializes as
         an explicit JSON `null`, not an omitted key. Both components checked
         `!== undefined`, which `null` sails straight past - `formatDurationNano(null)`
         then ran, and `null / 1_000_000` coerces to `0` in JS, printing "0µs". Fixed by
         checking `!= null` (loose equality, catches both) instead.
      2. **Product decision, not just a bug**: even with the null-check fixed, a
         live-tailed row's duration is *always* absent (per the "deliberately deferred"
         bullet above - it's not a rare miss, live-tail never populates this field at
         all), so a Duration column showing nothing but `—` for every visible row while
         live is worse than no column - direct user feedback after seeing it live.
         `LogTable.svelte`'s `COLUMNS` grid template became `$derived` on
         `explorer.live` (was a plain `const`) and both the header cell and each
         `LogRow`'s cell are now wrapped in `{#if !live}` (`live` newly passed down as a
         prop) - the column reappears the moment live tail is turned off, no reload
         needed. `EventDetailSheet`'s "Span duration" field keeps showing `—` while live
         (a single on-demand field isn't the same clutter a whole column is) - only the
         null-check fix applies there.
      `npm run check` (0 errors, 0 warnings) and `npm run build` (clean) after the fix.
- [x] ~~**Not yet done**: ... `EventDetailSheet`'s "Span duration" cell against a
      real correlated event, and that Patterns'/export's query cost is genuinely
      unaffected.~~ **Live e2e-verified 2026-08-22.** Opened a real event's detail
      sheet: "Span duration: 275.0ms" matched the table's own Duration column
      exactly (real `TraceId`/`SpanId`/`ParentId` shown alongside it, not
      placeholders). Confirmed the query-cost claim directly off the wire, not just
      by re-reading the source: captured the real request bodies via a live network
      trace - `POST /api/logs/patterns` sends `{"filter":{...}}` with no
      `includeSpanDuration` key at all; the export dialog's "All matching the
      filter" flow sends `POST /api/logs/search` with `{"filter":{...},
      "pageSize":1000}`, also no `includeSpanDuration`; only the main Logs Explorer
      table's own search request carries `"includeSpanDuration":true`. Exactly the
      three-way split the code intends. No bugs found.

### v20 — Prometheus native scrape (2026-08-22)

Not a prior "Later" item — proposed fresh, in answer to a direct "can we get metrics from
Prometheus?" question. Until now the only way in was OTLP push (`OtlpGrpcMetricsService`/
`OtlpHttpMetricsEndpoints`), which meant a Prometheus-shaped `/metrics` endpoint needed an
OpenTelemetry Collector in front of it (`prometheus` receiver → `otlp` exporter) to reach
Flare at all. This adds a second, pull-side receiver directly to `Flare.Ingest` that scrapes
configured targets itself.

Scoped down in one place, confirmed with the user before implementation
(`AskUserQuestion`): **backend only for v1** — no changes to the Ingestion page's stats/UI.
`IngestionProtocol` today hardcodes exactly 6 receiver rows (3 signals × {Grpc, Http}) across
a C# enum, `IngestionJsonContext`, the dashboard's TS type, and two Svelte components
(`IngestionReceivers.svelte`, `IngestionSignalsTable.svelte`); wiring a third `Scrape`
protocol through all of that is real, separate work, deferred rather than rushed in.

- [x] **`Flare.Ingest/Prometheus/`** — three new pieces, mirroring the existing OTLP
      metrics split (`OtlpHttpMetricsEndpoints` → `OtlpMetricsMapper` → `IMetricEventSink`):
      `PrometheusExpositionParser` (pure static parser for the classic text exposition
      format 0.0.4 — `# HELP`/`# TYPE`/`name{labels} value [timestamp]`; OpenMetrics-only
      features — exemplars, `# EOF`, UTF-8 quoted names, native histograms — out of scope,
      same "v1 doesn't cover X" precedent `OtlpMetricsMapper` already set for
      ExponentialHistogram/Summary; tolerant of malformed individual lines rather than
      throwing, since this runs against exporters Flare doesn't control), then
      `PrometheusMetricsMapper` (parsed samples + one target's identity → the same
      `Otlp.MetricMapResult` type `OtlpMetricsMapper` returns, reused rather than
      duplicated), then `PrometheusScrapeWorker` (a `BackgroundService`, one independent
      `PeriodicTimer` loop per configured target under one `Task.WhenAll`, writing through
      the exact same `IMetricEventSink` the OTLP receivers use — no new storage path, no
      new flush worker, scraped points flow through the identical Redis-stream/
      `MetricFlushWorker`/ClickHouse pipeline and show up on the Metrics page identically
      to pushed ones).
- [x] **Type mapping**: `counter` → `SumPointRecord` (always monotonic, always Cumulative
      temporality — Prometheus counters only ever count up since process start, there's no
      delta flavor on the wire). `gauge`/untyped → `GaugePointRecord` (untyped defaults to
      gauge per standard Prometheus consumer convention). `histogram` → `HistogramPointRecord`,
      **converting Prometheus's cumulative `_bucket{le=...}` counts into OTLP's
      non-cumulative `BucketCounts`** (`counts[i] - counts[i-1]`) — the single easiest place
      to get this wrong, covered by a worked-example unit test. `summary` → dropped, same
      treatment `OtlpMetricsMapper` already gives Summary/ExponentialHistogram on the OTLP
      side. Resource attribution (`service.name` = the target's configured `Job`,
      `service.instance.id` = the target URL's host:port, target `Labels` merged in last so
      they can override either) follows the OTel Collector's own `prometheusreceiver`
      convention, not a novel mapping.
- [x] **Config**: `PrometheusScrapeOptions`/`PrometheusScrapeTargetOptions`, bound from a
      `PrometheusScrape` section — same `IOptions<T>` convention as
      `MetricEventPipelineOptions`/`LogPatternOptions`. Deliberately no separate `Enabled`
      flag: an empty `Targets` list (the default) is already a no-op, matching Prometheus's
      own `scrape_configs: []` convention rather than a second switch that could disagree
      with an empty list. Each target has its own `Interval`/`Timeout`, optional `Labels`
      (extra/override resource attributes) and `Headers` (e.g. a bearer token for a
      protected endpoint).
- [x] **Deliberately deferred, not folded in here**: full Ingestion-page stats/UI
      integration — a third `IngestionProtocol.Scrape` value threaded through the C# enum,
      `IngestionJsonContext`, the dashboard's `IngestionProtocol` TS type, and a new row in
      both `IngestionReceivers.svelte` and `IngestionSignalsTable.svelte`. Until that lands,
      `PrometheusScrapeWorker` deliberately makes no `IIngestionStatsTracker` calls at all —
      tagging scrape activity onto the existing `Http` enum value would silently corrupt the
      real "HTTP :4318" receiver counters the Ingestion page already renders, which is worse
      than just not reporting scrape activity yet. Plain `ILogger` (accepted point counts,
      or failure + reason, per target) is v1's whole observability story for the scrape
      receiver itself.
- [x] **Verification performed**: `dotnet build` on `Flare.slnx`, and `dotnet test` on
      `Flare.Ingest.Tests` — 157 passed (up from 140), all 17 new in
      `PrometheusExpositionParserTests`/`PrometheusMetricsMapperTests` (histogram
      cumulative→delta worked example, label-set grouping excluding `le`, counter/gauge/
      untyped mapping, job/instance/Labels resource attribution and override order, summary
      → `UnsupportedMetricNames`, quoted-label escaping, malformed-line tolerance). No
      dedicated worker-loop test, matching this codebase's existing precedent
      (`MetricFlushWorker`/`SpanFlushWorker` have none either — only their pure pieces do).
      ~~**Not yet done**: a live e2e run against a real target...~~ **Live
      e2e-verified 2026-08-22** against a real `prom/node-exporter` container
      (`PrometheusScrape__Targets__0__*` env vars, no prior docker-compose.yml/
      .env.example wiring existed for this - added via a throwaway
      `docker-compose.override.yml`, not committed). Ingest logs confirmed real
      scrapes every 10s, correctly dropping the one genuinely-unsupported metric
      shape (`go_gc_duration_seconds`, a Summary) exactly per
      `PrometheusExpositionParserTests`' documented behavior. Real points landed in
      both `metrics_gauge` (`node_scrape_collector_success` etc.) and `metrics_sum`
      (`node_cpu_seconds_total`, 64 series) - confirmed via a direct ClickHouse
      query, not just app-level trust. Dashboard Metrics page picker listed the real
      metric names/service/type; charted `node_cpu_seconds_total` and got a real
      rendered line chart with a real legend (idle/iowait/irq/nice/softirq). No bugs
      found.
- [x] **Follow-up (2026-08-22): the deferred Ingestion-page stats/UI integration above,
      picked up the same day.** `IngestionProtocol` (both the `Flare.Ingest`/`Flare.Api`
      copies, kept in sync by convention like every other pairing between the two) gained
      a third `Scrape` member, and `PrometheusScrapeWorker` now calls
      `IIngestionStatsTracker.RecordAcceptedAsync`/`RecordRejectedAsync` (reasons
      `scrape-status:{code}`/`scrape-failed:{ExceptionType}`) tagged with it — a distinct
      protocol, not folded into `Http`, so it gets its own counters rather than corrupting
      the real "HTTP :4318" ones. Also wired `RecordServiceBreakdownAsync` (reusing
      `ServiceBreakdown.Build` unchanged), closing a related gap the backend-only scope
      left: scraped targets' services were invisible on the Service-breakdown panel too,
      not just absent from the receiver rows.
      `IngestionStatsQueryService`/`IngestionJsonContext` needed no code changes —
      `Signals`/`Protocols` are already derived via `Enum.GetValues<T>()`, so the response's
      dense per-minute bucket grid picked up the new value automatically (6 → 9 combos/min;
      the two new non-Metrics ones, Logs/Traces × Scrape, are always zero, harmless). Every
      UI surface that hardcoded exactly `{Grpc, Http}` got a third branch:
      `IngestionReceivers.svelte`/`IngestionSignalsTable.svelte` (`+1` row apiece,
      `IngestionSignalsTable.svelte` for `Metrics` only — Logs/Traces never scrape, so this
      is a 7th row, not a full 3rd signal column), the `ingestion` terminal command, and
      `Flare.Cli`'s `IngestionCommand` (a separate hand-port, no code-sharing boundary with
      the dashboard). Introduced one new shared helper,
      `ingestion/format.ts`'s `protocolLabel()`, deduplicating a short-label ternary that
      `RejectedTelemetryDialog.svelte`/`IngestionLog.svelte` (×2) had each copy-pasted
      before a third branch had to land in all three — the one new cross-cutting
      abstraction; the longer per-receiver-row labels (`"gRPC :4317"` etc.) stayed as
      independent local arrays per surface, following the precedent already set before
      Scrape existed rather than inventing a shared constant for those too.
      `IngestionChart.svelte`/`ingestion/health.ts` needed no changes at all — both are
      already signal-only/protocol-agnostic, so Scrape traffic flows into the existing
      per-signal chart lines and health/status computations automatically.
      `dotnet test`: `Flare.Ingest.Tests` 160 passing (up from 157, 3 new `Scrape`
      `IngestionStatsKeys.FieldPrefix` cases), `Flare.Api.Tests` 442 passing (no test
      added/removed — the one dense-bucket-count test's assertion updated from `3×3×2` to
      `3×3×3` in place). Dashboard:
      `npm run check` (0 errors/warnings) and `npm run build` clean.
      ~~**Not yet e2e-verified against a real scrape target/live stack**~~
      **Live e2e-verified 2026-08-22**, same pass as the parent v20 entry above.
      Ingestion page: a real "Prometheus scrape" row in the Receivers table (✓
      Healthy, 48 req - a 7th row, correctly distinct from gRPC :4317/HTTP :4318),
      and a real "Metrics · Prometheus scrape" row in the per-signal breakdown table
      (49 requests) - confirming both the receiver-status and per-signal-count
      surfaces this item added actually populate from real scrape traffic, not just
      compile. No bugs found.

Anything past v1 is intentionally vague. Decide based on whether people actually use v1.

---

## Non-goals (for now)

- Not an APM. Not a metrics platform. Not a SIEM.
- Not a crash-reporting tool (that's a different problem — symbolication, fingerprinting; use Sentry/GlitchTip for that).
- Not chasing feature parity with Datadog/Grafana/SigNoz. Different bet.

---

## Open questions to resolve before/early in v1

1. **Dashboard stack.** ~~Not committing to Blazor — the UI ambition may be better served by a dedicated SPA (Svelte / React) for virtualized tables and live-tail feel. Decision needed early, since the query API contract is shaped by it.~~ **Decided (2026-08-07): SvelteKit** (Svelte 5 runes, Tailwind 4, shadcn-svelte `mira` style, lucide icons), living at `src/dashboard`. Client-rendered SPA talking to `Flare.Api` over plain HTTP/WebSocket (`src/dashboard/src/lib/api.ts`) — the API stayed frontend-agnostic as intended, so this didn't require any query API changes.
2. **ClickHouse schema.** Fixed columns for the common fields (timestamp, level, service, message, trace/span id) + a flexible column strategy for arbitrary structured properties (Map vs. JSON vs. dynamic columns). Query performance depends on getting this right.
3. **Buffering layer.** ~~In-memory ring buffer for v1 simplicity, or Redis from the start for durability across restarts? Lean in-memory for v1; revisit.~~ **Decided (2026-08-07): Redis-backed from the start**, via `Aspire.Hosting.Redis` (`AddRedis(...).WithDataVolume().WithPersistence(...)`) + `Aspire.StackExchange.Redis` client, using Redis Streams (not `IDistributedCache`/`OutputCaching` — those are value-cache/HTTP-cache abstractions, not a fit) so events survive `Flare.Ingest` restarting mid-buffer. Consumer-group `XREADGROUP`/`XACK` gives at-least-once delivery into the ClickHouse flush. Valkey (`Aspire.Hosting.Valkey`, wire-compatible) noted as a cheap later swap if Redis's license becomes a concern for a bundled `docker-compose` dependency — not a v1 decision.
4. **OTLP transport priority.** Support both gRPC (4317) and HTTP (4318), or ship HTTP first and add gRPC fast-follow?
5. **Timestamp/timezone & clock-skew handling** from distributed clients.

---

## Tech stack summary

- **.NET** (latest LTS) — Aspire, ASP.NET Core
- **ClickHouse** — storage/query (via Aspire ClickHouse integration)
- **OpenTelemetry / OTLP** — the one ingestion protocol
- **SvelteKit** (Svelte 5) — dashboard SPA, Tailwind 4 + shadcn-svelte for UI
- **RustFS** — cold/object storage (Later)
- **Docker Compose** — v1 distribution

---

## Contributing

Pre-alpha; the architecture is still soft. If this README resonates, open an issue to discuss direction before large PRs. The fastest way to help early: pressure-test the ingestion assumptions and the ClickHouse schema.

## License

TBD — intended to be permissive open source (MIT or Apache-2.0).