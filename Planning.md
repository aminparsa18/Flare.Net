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
- [ ] Getting-started docs updated to show the `Flare.Aspire` path for Aspire-orchestrated
      consumers, alongside the existing per-logger (Serilog/NLog/ZLogger/MEL) snippets for
      non-Aspire consumers.

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

### Later (only if v1 gets traction)
- [ ] `dotnet tool install -g flare` CLI that scaffolds + launches the stack
- [ ] Retention policies + cold storage to S3-compatible object store (**RustFS**)
- [ ] Alerting (threshold/query-based → webhook, email, Slack)
- [ ] Auth + multi-user / roles
- [ ] OTLP traces & metrics (become a real observability tool)
- [ ] Trace/log correlation view
- [ ] Saved dashboards / shareable views
- [ ] Helm chart for Kubernetes

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