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
      `v*.*.*` git tags. Build-only (no push) on PRs. Single-arch (`linux/amd64`) for
      now — pre-alpha, no evidence yet anyone needs arm64; buildx is wired in from the
      start so adding `linux/arm64` later is a one-line change. **Done and verified
      2026-08-07** — merged via PR #8, `:edge` confirmed live and public on all three
      Docker Hub repos.
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
- [ ] `dotnet tool install -g flare` CLI that scaffolds + launches the stack
- [ ] Retention policies + cold storage to S3-compatible object store (**RustFS**)
- [ ] Auth + multi-user / roles
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
- [ ] Helm chart for Kubernetes
- [x] ~~**Ingestion page: pipeline health.** Scoped out of v8's MVP on purpose -
      throughput/rejected-payload stats (v8) answer "is data arriving"; this answers "is
      the buffered pipeline keeping up," which needs its own design pass.~~ **Promoted and
      shipped 2026-08-10 (see v10 below)** - the design pass landed the same day it was
      requested rather than waiting for a real incident, since the user asked for it
      directly.

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
4. **Known, deliberately-unresolved limitation** (flagged, not solved, same convention
   v6's rate-calc/quantile-interpolation notes use): `PipelineStreamKeys` (`Flare.Api`)
   hardcodes each signal's stream key/consumer-group name as `LogEventPipelineOptions`/
   `SpanEventPipelineOptions`/`MetricEventPipelineOptions`'s *default* values
   (`flare:logs`/`flare-ingest`, `flare:spans`/`flare-ingest-spans`,
   `flare:metrics`/`flare-ingest-metrics`) - a deployment that overrides them via its own
   config won't have its stream health picked up here, since the two processes share no
   config source (same "different deployables, no reference" situation every other
   Flare.Ingest/Flare.Api pairing is already in). This is also the one place that bridges
   the two existing, slightly mismatched vocabularies: the OTLP-facing `IngestionSignal`
   enum (v8) calls the second signal "Traces"; the pipeline layer calls it "spans"
   (`SpanFlushWorker`, `flare:spans`) - `PipelineStreamKeys`/`FlushHealthKeys` key off the
   former throughout, joining the whole feature on one vocabulary.

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