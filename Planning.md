# Flare

**A self-hosted, OpenTelemetry-native log dashboard for .NET developers — with a UI that doesn't feel like it's from 2014.**

Flare is an open-source log ingestion server and dashboard. Point any OTLP-capable logger at it and get a fast, modern, genuinely nice place to search, filter, and live-tail your structured logs. Self-hosted, low-overhead, and designed to be running in one command.

> Status: **Planning / pre-alpha.** This document is the design contract for v1. Nothing here is built yet.

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
  │  • Buffer + batch (in-memory / Redis)         │
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
| `Flare.Dashboard` | SPA (see open question) | The UI — the thing people come for |
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
- [ ] Batched insert pipeline (buffer, flush by size/interval)
- [ ] Query API: search, filter, time-range, aggregate
- [ ] Live-tail streaming endpoint
- [ ] Dashboard: log table, live tail, filtering, event detail, basic volume chart
- [ ] `docker-compose.yml` — full stack up in one command
- [ ] Getting-started docs with a snippet per logger (Serilog, NLog, ZLogger, MEL)

### Later (only if v1 gets traction)
- [ ] `dotnet tool install -g flare` CLI that scaffolds + launches the stack
- [ ] Official `Aspire.Hosting.Flare` integration package — lets any .NET developer
      already using .NET Aspire for their own app add Flare as a dev-time resource in
      *their* AppHost (`builder.AddFlare("flare")`, mirroring `Aspire.Hosting.Seq` /
      `Aspire.Hosting.Redis` / `CommunityToolkit.Aspire.Hosting.*`), instead of running
      `docker compose up` by hand. Not our own internal `Flare.AppHost` (that stays
      Flare's private dev-inner-loop orchestrator, never meant for end users) — this
      would be a separate, publishable package wrapping Flare's published container
      image(s). **Gated on the `docker-compose.yml` v1 item landing first** — there's
      nothing to wrap into a resource until an official image exists.
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

1. **Dashboard stack.** Not committing to Blazor — the UI ambition may be better served by a dedicated SPA (Svelte / React) for virtualized tables and live-tail feel. **Decision needed early**, since the query API contract is shaped by it. (Keep the API frontend-agnostic regardless.)
2. **ClickHouse schema.** Fixed columns for the common fields (timestamp, level, service, message, trace/span id) + a flexible column strategy for arbitrary structured properties (Map vs. JSON vs. dynamic columns). Query performance depends on getting this right.
3. **Buffering layer.** In-memory ring buffer for v1 simplicity, or Redis from the start for durability across restarts? Lean in-memory for v1; revisit.
4. **OTLP transport priority.** Support both gRPC (4317) and HTTP (4318), or ship HTTP first and add gRPC fast-follow?
5. **Timestamp/timezone & clock-skew handling** from distributed clients.

---

## Tech stack summary

- **.NET** (latest LTS) — Aspire, ASP.NET Core
- **ClickHouse** — storage/query (via Aspire ClickHouse integration)
- **OpenTelemetry / OTLP** — the one ingestion protocol
- **SPA framework — TBD** (see open questions)
- **RustFS** — cold/object storage (Later)
- **Docker Compose** — v1 distribution

---

## Contributing

Pre-alpha; the architecture is still soft. If this README resonates, open an issue to discuss direction before large PRs. The fastest way to help early: pressure-test the ingestion assumptions and the ClickHouse schema.

## License

TBD — intended to be permissive open source (MIT or Apache-2.0).