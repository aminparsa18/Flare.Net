# Architecture

Flare is a self-hosted, OpenTelemetry-native observability platform for
.NET — logs, traces, metrics and profiles as first-class citizens,
correlated in one place, with alerting (thresholds, anomalies, SLO burn
rates), error tracking, and dashboards on top. This page explains why it's
built the way it is; see [`../reference/`](../reference/) for exact values
and [`../how-to/`](../how-to/) for task instructions.

## Why Flare exists

The self-hosted log tooling landscape for .NET devs is a choice between
two frustrations: **Seq** — a solid engine, but a dated UI and a
single-user free tier — and **Grafana / SigNoz / OpenObserve** — powerful
and OTel-native, but heavy to run, OTel-generic rather than tuned to the
.NET dev experience, and built for infra/SRE teams rather than a developer
who just wants to read their app's telemetry beautifully.

Flare's bet: **the storage/query problem is already solved by ClickHouse
(see [ADR-0013](../../docs-internal/adr/0013-clickhouse-as-storage-engine.md)).
The differentiator is a great dashboard and a two-minute setup.** That's
where the effort goes — Flare isn't chasing feature parity with a full
observability suite; see [Non-goals](#non-goals) below.

## Design principles

1. **One protocol in: OTLP.** No ingestion adapters per logging library —
   every supported logger, and every other signal, reaches Flare through the
   OpenTelemetry Protocol. The one other way in, a Prometheus scrape
   receiver, exists for exporters that only speak Prometheus. See
   [ADR-0012](../../docs-internal/adr/0012-otlp-only-ingestion.md).
2. **Storage is a solved problem.** ClickHouse is the backend, wrapped
   well rather than reinvented. See
   [ADR-0013](../../docs-internal/adr/0013-clickhouse-as-storage-engine.md).
3. **The dashboard is the product.** Frontend gets first-class attention
   — it should look and feel unlike Seq or a Grafana panel. See
   [ADR-0001](../../docs-internal/adr/0001-sveltekit-dashboard.md) for the
   stack behind it.
4. **Least overhead to try.** `docker compose up` for the standalone path;
   a `flare` CLI for a standing instance. If trying Flare takes more than
   two minutes, that's a bug in the getting-started experience.
5. **.NET-first DX, not .NET-only.** The getting-started experience is
   tailored to .NET loggers, Aspire and .NET MAUI, but because ingestion is
   pure OTLP, polyglot teams aren't locked out.
6. **Compute once, read many times.** Work that every page load would
   repeat (RED metrics, the service map, SLO counters, log patterns) is done
   at insert time, and every query is capped so one bad filter can't take
   the database down. See
   [How Flare stores and queries telemetry](how-flare-stores-and-queries-telemetry.md).
7. **You hold the data.** Self-hosted means telemetry stays on your
   infrastructure, and anything that could leave it (the update check, AI
   providers, notification channels, forwarding) is opt-in or visible. See
   [Data residency and privacy](data-residency-and-privacy.md).
8. **Scope discipline.** Ship a tight, focused feature set. Speculative
   ideas go in [the roadmap](../../docs-internal/planning/roadmap.md), not
   into whatever's currently shipping.

## The ingest → dashboard pipeline

```
  ┌─────────────────────────────────────────────┐
  │  Your apps / services                        │
  │  Serilog · NLog · ZLogger · MS ILogger · any │
  │  OTLP source (Go, Node, Python, browsers,    │
  │  .NET MAUI, OpenTelemetry Collector, ...)    │
  └───────────────────────┬─────────────────────┘
                          │  OTLP  (gRPC :4317 / HTTP :4318)
                          │  (+ Prometheus scrape, pulled)
                          ▼
  ┌─────────────────────────────────────────────┐
  │  Flare.Ingest  (ASP.NET Core)                │
  │  • OTLP receiver (logs, traces, metrics,      │
  │    profiles)                                  │
  │  • Ingest-key auth and limits                 │
  │  • Normalize → internal event model           │
  │  • Redaction/extraction rules, trace sampling │
  │  • Buffer (one Redis Stream per signal)       │
  │  • Flush workers: batch-insert, then ack      │
  └───────────────────────┬─────────────────────┘
                          │  batched inserts
                          ▼
  ┌─────────────────────────────────────────────┐
  │  ClickHouse   (columnar store)               │
  │  telemetry · pre-aggregates · config tables  │
  └───────────┬───────────────────────┬─────────┘
              │  SQL                  │  SQL
              ▼                       ▼
  ┌──────────────────────────┐  ┌────────────────────────┐
  │  Flare.Api               │  │  Flare.AlertWorker     │
  │  • Search / aggregate    │  │  • Evaluates alert     │
  │  • Live tail (WebSocket) │  │    rules               │
  │  • Config CRUD, auth     │  │  • Sends notifications │
  │  • MCP, Prometheus/Loki/ │  │  • Synthetic probes,   │
  │    Tempo-compatible APIs │  │    reports, archive    │
  └────────────┬─────────────┘  └────────────────────────┘
               │  HTTP / WS
               ▼
  ┌─────────────────────────────────────────────┐
  │  Flare.Dashboard  (SPA)                      │
  │  • Logs, Traces, Errors, Metrics, Alerts,    │
  │    Dashboards, and more                      │
  │  • Live tail · saved views · charts          │
  └─────────────────────────────────────────────┘
```

Three read paths leave the Redis buffer or ClickHouse, and none of them
slows ingestion down: queries go to ClickHouse, live tail reads the same
Redis Stream ingest writes (so new lines show up before they're flushed),
and alert evaluation runs in its own process.

| Component | Tech | Responsibility |
|---|---|---|
| `Flare.Ingest` | ASP.NET Core | Terminate OTLP (gRPC + HTTP) for logs, traces, metrics and profiles, plus the screenshot and feedback endpoints `Flare.Maui` uses. Authenticate ingest keys and enforce their limits, map to the internal event model, apply [pipeline rules](../how-to/data/manage-pipeline-rules.md), log-pattern clustering and trace sampling, buffer, batch-insert to ClickHouse. Also scrapes Prometheus endpoints and can forward telemetry to another OTLP endpoint |
| Redis | container | Durable buffer between ingest and ClickHouse — see [ADR-0002](../../docs-internal/adr/0002-redis-streams-buffering.md). Also the query-result cache ([ADR-0029](../../docs-internal/adr/0029-query-result-caching.md)) and the lock and markers alert evaluation uses to run on one replica per tick |
| ClickHouse | container | Log/trace/metric/profile storage and query engine, the pre-aggregated tables the Services and SLO pages read, and the configuration tables (alert rules and their history, dashboards, saved views, SLOs, status pages) — see [ADR-0013](../../docs-internal/adr/0013-clickhouse-as-storage-engine.md) |
| `Flare.Api` | ASP.NET Core | Query/search/aggregate over ClickHouse; live-tail streaming endpoint; CRUD for everything configurable; authentication endpoints; the read-only [MCP](../how-to/integrations/connect-ai-assistants.md) endpoint and Prometheus-, Loki- and Tempo-compatible query APIs |
| `Flare.AlertWorker` | ASP.NET Core worker | Evaluates alert rules and sends notifications, runs synthetic probes, and (when enabled) renders scheduled dashboard reports and archives telemetry to S3-compatible storage. Split out of `Flare.Api` so an API deploy doesn't pause alerting — see [ADR-0018](../../docs-internal/adr/0018-alert-worker-extraction.md) |
| `Flare.Identity` | library | Users, sessions, personal access tokens, ingest keys, projects, the audit log and source maps. Linked into both `Flare.Ingest` and `Flare.Api`, stored in an embedded SQLite file by default or in Postgres — see [ADR-0004](../../docs-internal/adr/0004-embedded-sqlite-for-identity.md) and [ADR-0111](../../docs-internal/adr/0111-pluggable-identity-store-postgres.md) |
| `Flare.Dashboard` | SvelteKit (Svelte 5, runes) + Tailwind + shadcn-svelte | The UI — the thing people come for. See [ADR-0001](../../docs-internal/adr/0001-sveltekit-dashboard.md). |
| `Flare.AppHost` | .NET Aspire | Local orchestration of all of the above |
| `Flare.Cli`, `Flare.Mcp` | .NET global tool | The `flare` CLI that manages a standing instance and queries it from a terminal; `flare mcp` is the same read-only tools served over stdio |
| `Flare.Maui` | NuGet package | `UseFlare()` for .NET MAUI apps: sessions, crashes, hangs, startup performance, breadcrumbs — see [ADR-0166](../../docs-internal/adr/0166-maui-sdk.md) |

Flare is still four things to run (Ingest, Api, AlertWorker, Dashboard) and
two backing stores (ClickHouse, Redis); Identity adds a SQLite file to
those, not a fifth container. An optional object store (RustFS in the
bundled Compose and Aspire setups) backs the
[cold tier](retention-and-tiering.md) and the archive, and an optional,
read-only Docker socket proxy feeds the Resources page.

Every OTLP-capable .NET logger reaches Flare's ingest receiver the same
way — see [`../how-to/deploy/run-standalone.md`](../how-to/deploy/run-standalone.md)
for a copy-paste snippet per logger
(`Microsoft.Extensions.Logging`/ZLogger via `OpenTelemetry.Exporter.OpenTelemetryProtocol`,
Serilog via `Serilog.Sinks.OpenTelemetry`, NLog via
`NLog.Targets.OpenTelemetryProtocol`) and
[`../reference/otlp-logger-versions.md`](../reference/otlp-logger-versions.md)
for known-good package versions.

### Where to read more

| Question | Page |
|---|---|
| What happens to data when Ingest restarts or ClickHouse is down? | [Ingest reliability and delivery guarantees](ingest-reliability-and-delivery-guarantees.md) |
| Why ClickHouse, how are tables shaped, what keeps queries bounded? | [How Flare stores and queries telemetry](how-flare-stores-and-queries-telemetry.md) |
| How does a rule go from breach to notification to resolved? | [How alerting works](how-alerting-works.md) |
| What does sampling, pattern clustering or cardinality cost? | [Sampling, clustering and cost](sampling-clustering-and-cost.md) |
| How long is data kept, and what is the cold tier? | [Retention and tiering](retention-and-tiering.md) |
| How do logins, tokens and ingest keys work? | [Authentication model](authentication-model.md) |
| How do I limit a team to its own services? | [Projects and access scoping](projects-and-access-scoping.md) |
| How do I run more than one ClickHouse node? | [Clustering](clustering.md) |
| Where does my data go, and what can leave the machine? | [Data residency and privacy](data-residency-and-privacy.md) |

## Non-goals

- Not an APM agent. Flare doesn't instrument your code or ship an agent; it
  receives what OpenTelemetry sends. It does distributed tracing, continuous
  profiling (the OTLP profiles signal, still Alpha in OpenTelemetry) and
  release tracking from the data you send.
- Not a SIEM. There is an audit log and ingest-time redaction, but no
  security analytics or correlation rules.
- Not a Sentry or GlitchTip replacement for every platform. Error tracking
  is built from OTel exceptions, grouped on the [Errors page](#errors) with
  .NET and JavaScript source-map symbolication, and `Flare.Maui` and the
  [browser replay recorder](../how-to/instrument/send-browser-telemetry.md#record-session-replay) are the dedicated
  client SDKs; other platforms send OTLP.
- Not a drop-in Prometheus, Loki or Tempo. The compatible APIs are read-only
  and cover a subset of PromQL, LogQL and TraceQL, enough for Grafana to
  query Flare, not to replace those servers.
- Not chasing feature parity with Datadog/Grafana/SigNoz. Different bet —
  see [Why Flare exists](#why-flare-exists) above.

## Built with

.NET/ASP.NET Core, .NET Aspire, ClickHouse, Redis, OpenTelemetry/OTLP,
SvelteKit (Svelte 5) + Tailwind + shadcn-svelte, SQLite or Postgres for
identity, Docker Compose and Helm. Optional pieces: RustFS (S3-compatible)
for the cold tier and archive, and Chromium (via Playwright) for emailed
dashboard reports. The Grafana data source plugin is TypeScript with a Go
backend.

## Ways to run Flare, and why each exists

Flare has three legitimate local install paths, each solving a different
problem rather than being redundant with the others, and a fourth for
production clusters:

- **[.NET Aspire](../how-to/deploy/run-with-aspire.md)** (`Flare.Hosting.Aspire`) —
  for an app that already has an AppHost. Flare joins the resource graph;
  `aspire start` already orchestrates its lifecycle alongside everything
  else.
- **[Standalone Docker Compose](../how-to/deploy/run-standalone.md)** — for a
  one-off, repo-local evaluation. `docker compose up` at the repo root is
  the fastest way to just look at Flare once.
- **[The `flare` CLI](../how-to/deploy/run-with-cli.md)** (`Flare.Cli`, from
  NuGet or npm) — for a standing instance you start once and forget about,
  from any directory, shared across many unrelated local projects,
  independent of any single AppHost's lifecycle. This is the case the other
  two paths structurally can't cover: Aspire mode ties Flare's lifecycle to
  one AppHost, and a repo-local Compose stack isn't meant to run for weeks
  in the background serving unrelated projects.
- **[Kubernetes with Helm](../how-to/deploy/deploy-with-helm.md)** — for a
  shared or production install: the whole stack from one chart, with an
  Ingress and generated passwords. For more than one ClickHouse node see
  [cluster mode](../how-to/deploy/run-cluster-mode.md) and
  [Clustering](clustering.md).

## Tour of the dashboard

Whichever install path you pick, you land in the same place:
`Flare.Dashboard` — a single SvelteKit SPA, all talking to `Flare.Api` over
HTTP/WebSocket, with no separate tools for logs, traces, metrics, errors, or
alerting. First visit creates the admin account (see
[`../how-to/admin/configure-authentication.md`](../how-to/admin/configure-authentication.md));
after that it's a normal login.

The nav bar carries the pages used most (Logs, Traces, Errors, Messaging,
Metrics, Ingestion, Alerts, Resources, Dashboards, Views, and Auth for
admins); Releases, Frontend, External APIs, Sessions, LLM, Profiles, Hosts
and Kubernetes sit in a "more" menu. Configuration lives under **Settings**,
described [at the end of the tour](#settings). `Ctrl+K` (`⌘K` on macOS) opens a command
palette that jumps to any of them.

### Logs

The default view (`/`). A dense, virtualized log table with live tail
(real-time streaming, pause/resume), an event-volume chart, and filters
for service, level, scope, attributes and free-text search over the message
body. Click any row to expand its full structured payload, scopes, and
exception details.

![Logs Explorer](../screenshots/logs-en.webp)

### Traces

`/traces` — every OTLP trace Flare has received, filterable by service and
time range. Auto-instrumented spans (ASP.NET Core, HttpClient, …) and
anything your own code emits via `ActivitySource` show up side by side.
The list is newest-first by default; click the Time, Duration, or Spans
column header to sort by that column instead (click again to reverse), e.g.
the slowest requests in the window, or the traces with the most spans.

![Traces list](../screenshots/traces.png)

Click into a trace for the waterfall view — parent/child spans laid out by
start time and duration, the same shape as Jaeger/Zipkin but wired
straight into the rest of the dashboard. Span events show up as dots on
the span's bar at the moment they happened (exceptions in red); hover one
for its name, offset, and key attributes. The "Flame graph" toggle shows
the same spans as a flame graph (width = duration, stacked by depth,
colored by service) to see where time went in a large trace at a glance;
click a span for its details, double-click to zoom into it. On either view,
the "Search spans" box finds spans by name, service, span id, or attribute
(`key=value` works too): matches are highlighted, everything else is dimmed,
and Enter / Shift+Enter step through them, expanding collapsed parents on
the way.

![Trace detail waterfall](../screenshots/traces-en.webp)

A "Services" tab alongside the trace list gives a sortable, per-service
rollup of the RED metrics (request rate, error rate, p50/p95/p99 latency)
derived from the same spans, over its own selectable window (5m–24h),
refreshed every 10 seconds. Answers "which service is unhealthy right now"
without hand-building the equivalent query in the trace search or the
Metrics picker. "Requests" here means root spans (`ParentSpanId` empty) —
a service's trace entry points, the same convention the trace list itself
uses for "one row per trace". Click a service name to jump back into the
trace list, pre-filtered to that service and window. The rollup reads a
table filled at insert time rather than scanning spans on every refresh
([ADR-0030](../../docs-internal/adr/0030-service-red-metrics-pre-aggregation.md)),
and the same idea drives the service dependency map and call breakdown
([ADR-0031](../../docs-internal/adr/0031-service-map-breakdown-pre-aggregation.md)).

A "Funnels" tab (`/traces/funnels`) follows traces through an ordered list
of span-match steps (service, span name, attribute filters) and shows how
many reach each step, where they drop off, errors per step and step-to-step
latency. Each figure opens the traces behind it. Computed at query time from
the stored spans, and saved as ordinary saved views. See
[`../how-to/investigate/analyze-trace-funnels.md`](../how-to/investigate/analyze-trace-funnels.md)
and [ADR-0067](../../docs-internal/adr/0067-trace-funnels.md).

A "Structure" button in the trace list's toolbar filters by how a trace's
spans relate: lettered span conditions combined with `A -> B` (direct
child), `A => B` (any descendant), AND, OR and NOT, for example "a
`checkout` span with a descendant `payment` span that errored". It's a
`SpanFilter` field, evaluated at query time as a `TraceId` subquery, so
the list, facets, saved views and dashboard panels all respect it. See
[`../how-to/investigate/find-traces-by-structure.md`](../how-to/investigate/find-traces-by-structure.md)
and [ADR-0069](../../docs-internal/adr/0069-structural-trace-queries.md).

Two more views read the same spans. An "N+1" view lists traces where one
parent span ran 10 or more identical database statements (see
[`../how-to/investigate/find-n-plus-one-queries.md`](../how-to/investigate/find-n-plus-one-queries.md)),
and a span's detail can show whether its duration was slow *for that kind of
span* (see [`../how-to/investigate/compare-span-duration.md`](../how-to/investigate/compare-span-duration.md)).

### Errors

`/errors` — exceptions grouped by type and normalized message (ids, numbers and quoted values collapsed), with counts, the
services and versions they appear in, and a sample stack trace. Each group
has a lifecycle: mark it resolved or ignored, assign it to someone, link it
to an issue in your tracker, and Flare tells you when a "fixed" error comes
back (a regression). Ownership rules (by service, exception type or source
file path) route a group to the right person, and stack frames can link to
the exact line in your repository, or show the source inline. The list is
built from `exception` events recorded on spans, so it works for any OTLP
sender that records exceptions on its spans. See
[`../how-to/investigate/triage-errors.md`](../how-to/investigate/triage-errors.md),
[`../how-to/investigate/link-exceptions-to-source-code.md`](../how-to/investigate/link-exceptions-to-source-code.md)
and [ADR-0121](../../docs-internal/adr/0121-error-issue-lifecycle.md).

### Releases

`/releases` — mark that a version of a service went out (from your pipeline,
with a personal access token), and Flare lists, per version, the exception
groups that first appeared in it, with release markers drawn on charts and
alerts for a release that introduces errors. To compare two versions of a
service side by side (latency, errors, throughput, new exceptions and new
outbound dependencies), open a service from the Services tab on Traces. See
[`../how-to/investigate/track-releases.md`](../how-to/investigate/track-releases.md),
[`../how-to/investigate/compare-deploys.md`](../how-to/investigate/compare-deploys.md)
and [ADR-0182](../../docs-internal/adr/0182-release-tracking.md).

### Frontend

`/frontend` — what browser apps report through the OpenTelemetry JavaScript
SDK: page loads, route changes, Core Web Vitals and JavaScript errors, with
minified stack traces mapped back to source from uploaded source maps. The
browser's spans share a trace id with your backend, so a slow click can be
followed into the API call and its queries. Browsers post to the OTLP/HTTP
receiver, so an ingest key can be limited to the origins allowed to use it.
See [`../how-to/instrument/send-browser-telemetry.md`](../how-to/instrument/send-browser-telemetry.md)
and [ADR-0153](../../docs-internal/adr/0153-frontend-page-and-source-map-settings.md).

### Sessions

`/sessions` — one row per app session from `Flare.Maui`: device, OS and app
version, whether it ended in a crash or hang, a per-session timeline of
screens, breadcrumbs, network calls and errors, optional screenshots and
user feedback, and startup and screen-load performance. The page also shows
release health: the crash-free share of sessions and users per app version
([ADR-0175](../../docs-internal/adr/0175-release-health.md)). See
[`../how-to/instrument/send-maui-telemetry.md`](../how-to/instrument/send-maui-telemetry.md),
[`../tutorials/monitor-a-maui-app.md`](../tutorials/monitor-a-maui-app.md) and
[ADR-0167](../../docs-internal/adr/0167-app-sessions-view.md).

### Messaging

`/messaging` — one row per Kafka topic, RabbitMQ queue, NATS subject or
Service Bus entity (MassTransit message types included), with publish and
consume rates, error rate, p99 latency and a backlog: messages waiting, as
Kafka consumer lag, RabbitMQ queue depth, NATS JetStream pending messages,
Service Bus active messages or SQS visible messages. The rates and latencies
are computed from producer and consumer spans' OTel `messaging.*` attributes
at query time, so there's no extra agent or table. The backlog comes from
metrics your broker or the collector already exports (for example its
`kafkametrics` or `rabbitmq` receiver) when they're present.
Click a row for its producers, consumers, partitions, per-group lag or
per-queue depth. See [`../how-to/monitor/monitor-message-queues.md`](../how-to/monitor/monitor-message-queues.md),
[ADR-0056](../../docs-internal/adr/0056-messaging-queue-monitoring.md) and
[ADR-0057](../../docs-internal/adr/0057-rabbitmq-queue-depth.md).

### External APIs

`/external-apis` — one row per external domain your services call, with
rate, error rate, p95/p99 latency, endpoint count and last seen. It's
computed at query time from `CLIENT` spans' `server.address` (else
`net.peer.name`, else the URL's host), leaving out database and messaging
calls. Click a domain for its endpoints (method plus `url.template`, or the
URL path with id-like segments collapsed to `{id}`), status codes, top
errors and calling services, each linking to matching traces. The Services
breakdown's External-calls tab falls back to the same domain when a span
has no `peer.service`. The Service Map adds a leaf node for each host that
outbound calls reached with no instrumented child span, read from the
`outbound_calls` table (migration 0035) anti-joined against spans' parent
ids. See [`../how-to/monitor/monitor-external-apis.md`](../how-to/monitor/monitor-external-apis.md),
[ADR-0071](../../docs-internal/adr/0071-external-api-monitoring.md) and
[ADR-0072](../../docs-internal/adr/0072-external-host-map-leaves.md).

### LLM

`/llm` — how your services use language models, from spans that follow the
OpenTelemetry GenAI conventions: per provider and model, call rate, error
rate, latency and input and output tokens, with an estimated cost from a
price table, and one click to the matching traces. A pre-aggregated table
keeps it fast. See [`../how-to/monitor/monitor-llm-calls.md`](../how-to/monitor/monitor-llm-calls.md),
[ADR-0100](../../docs-internal/adr/0100-llm-observability-genai-spans.md) and
[ADR-0101](../../docs-internal/adr/0101-llm-estimated-cost.md).

### Profiles

`/profiles` — continuous profiling: flame graphs of where CPU time or
memory goes, from the OTLP profiles signal on the same ports as the other
signals. OTLP profiles is Alpha in OpenTelemetry and few senders emit it
yet, so treat this page as early. See
[`../how-to/investigate/profile-with-continuous-profiling.md`](../how-to/investigate/profile-with-continuous-profiling.md)
and [ADR-0141](../../docs-internal/adr/0141-continuous-profiling-ingest.md).

### Metrics

`/metrics` — every OTLP metric instrument (Sum, Gauge, Histogram, exponential Histogram) reported
by your services, browsable from a searchable sidebar and rendered as a
time-series chart per instrument. Covers both the free
`AddAspNetCoreInstrumentation()`/`AddRuntimeInstrumentation()` data (.NET
GC, thread pool, Kestrel, HTTP client/server) and anything your own
`Meter` emits. Charts support group-by, formulas across queries, functions
such as rate, smoothing and time-shift, and exemplar dots that link a point
to a trace. Counters are shown as a reset-aware rate or increase. A
Catalog view (`/metrics/catalog`) lists every metric with its type, unit,
description and series count, lets you override a unit or description, and
helps find high-cardinality metrics. Prometheus scrape targets and
log-based metrics land in the same tables, so they chart and alert the same
way. Reading the same data from Grafana is covered under
[Integrations](#integrations-and-apis).

![Metrics browser](../screenshots/metrics-en.webp)

### Dashboards

`/dashboards` — your own boards of panels. A panel is a chart over logs,
traces or metrics (time series, bar, value, pie, table, histogram and
heatmap), a formula across metric queries, or text. Boards have variables
(multi-value, and chained so one picks from another), collapsible rows,
tags, a home-dashboard choice, per-panel thresholds, and an owner, so one
person's board isn't silently edited by another. A board can be duplicated,
exported as JSON, exported as Grafana JSON, started from a built-in
template, and emailed on a schedule. Clicking a panel opens the same query
in the Logs, Traces or Metrics explorer. See
[`../how-to/dashboards/build-custom-dashboards.md`](../how-to/dashboards/build-custom-dashboards.md),
[`../how-to/dashboards/schedule-dashboard-reports.md`](../how-to/dashboards/schedule-dashboard-reports.md),
[`../tutorials/first-dashboard.md`](../tutorials/first-dashboard.md) and
[ADR-0023](../../docs-internal/adr/0023-custom-dashboards.md).

### Resources, Hosts and Kubernetes

Three pages for the infrastructure under your services, and they answer
different questions:

- **Resources** (`/resources`) — what Flare discovers itself: its own Docker
  containers or Kubernetes pods and the machine it runs on. In Docker
  Compose it needs an optional read-only Docker socket proxy, off unless you
  enable it
  ([ADR-0005](../../docs-internal/adr/0005-docker-socket-proxy-for-resources-page.md));
  on Kubernetes it reads pods and services through RBAC limited to those two
  ([ADR-0006](../../docs-internal/adr/0006-kubernetes-resource-graph-rbac-scoping.md)).
- **Hosts** (`/hosts`) — one row per machine that reports its own
  `hostmetrics`: CPU, memory, disk and load, with a drill-down chart per
  metric. See [`../how-to/monitor/monitor-hosts.md`](../how-to/monitor/monitor-hosts.md).
- **Kubernetes** (`/kubernetes`) — your cluster as the collector's
  `kubeletstats` and `k8s_cluster` receivers report it, in Nodes, Namespaces,
  Workloads, Pods and Volumes tabs, with a link from a pod to its logs. See
  [`../how-to/monitor/monitor-kubernetes.md`](../how-to/monitor/monitor-kubernetes.md).

### Ingestion

`/ingestion` — operational visibility into the OTLP receiver itself:
current arrival/ingestion rates, a per-signal (logs/traces/metrics) ×
per-protocol (gRPC/HTTP) breakdown of requests, events, and bytes, plus an
ingestion log of anything Flare rejected (bad payloads, unsupported media
types) and why. The page to check first if "my logs aren't showing up."

![Ingestion](../screenshots/ingestion-en.webp)

![Ingestion pipeline health](../screenshots/ingestion-2-en.webp)

### Alerts

`/alerts` — alert rules over the same filters and queries the explorers
use, so what you can search for, you can alert on. A rule's condition is one
of:

- a **log count** (a saved log filter plus a count threshold on a rolling
  window);
- a **metric threshold** (a metric query compared with a value, with
  Last/Min/Max for gauges and a reset-aware increase for counters);
- an **exception count**;
- an **anomaly**, scored against the same window on previous days or weeks
  ([ADR-0048](../../docs-internal/adr/0048-anomaly-detection-alerting.md));
- an **SLO burn rate**, fired when an SLO's error budget burns too fast over
  both a long and a short window
  ([`../how-to/alerting/define-slos.md`](../how-to/alerting/define-slos.md),
  [ADR-0108](../../docs-internal/adr/0108-slo-error-budgets.md));
- **release errors**, when a release introduces new exception groups, and a
  **crash-free rate** for mobile versions.

Every rule has a cooldown and a "test against current data" dry-run before
saving. Each rule is checked every alert-worker poll tick (30s) by default, or on its
own "Evaluate every" interval (1m-1h) for slow, expensive rules.
Rules can also:

- require a minimum number of data points in a metric rule's window; with
  fewer, it reports insufficient data and doesn't fire;
- alert on absent data, when the condition matches nothing at all;
- set a recovery threshold (for example, fire at 90%, recover below 80%) so
  a value hovering around the threshold doesn't flap between firing and
  resolved;
- carry a severity (critical, error, warning or info; critical by default),
  sent as PagerDuty's severity, shown on other channels' notifications, and
  available to templates as `{{severity}}`;
- take a threshold in a different unit than the metric's (`500 ms` against a
  metric recorded in seconds), converted before each comparison (see
  [ADR-0080](../../docs-internal/adr/0080-alert-threshold-unit.md));
- carry your own key/value labels (`team=payments`), filterable in the rules
  list and available as `{{labels.team}}` and in webhook payloads (see
  [ADR-0084](../../docs-internal/adr/0084-alert-rule-labels.md)).

A rule can override its notification title/body with a template
using placeholders such as `{{value}}`, `{{threshold}}`, `{{logs_url}}`,
`{{log_samples}}` (the newest matching log lines of a log-count alert),
or `{{labels.service.name}}`, previewed live in the rule form. Templates can
be shared across rules from Settings > Workspace > Alert templates. With a
public URL configured, each notification links to the data that fired: the
matching logs, the metric's chart, or the matching exceptions, scoped to the
rule's filter over the evaluated window (`{{data_url}}` in a template), and
can carry a signed acknowledge link (`{{ack_url}}`).

What happens after a rule fires:

- **Notification.** A rule notifies one or more notification channels,
  managed from Settings > Workspace > Channels (or created on the spot with
  the rule form's "New channel" button): reusable, named destinations a
  rule references by ID rather than embedding inline, so the same channel
  can be reused across rules and a rule can fan out to more than one
  destination for the same breach (see
  [ADR-0021](../../docs-internal/adr/0021-reusable-notification-channels.md)).
  Channel types are webhook/Slack, Telegram, email, PagerDuty, Microsoft
  Teams, Discord, Jira, incident.io and Jira Service Management Ops.
  A rule created before channels existed still notifies through its own
  inline channel unchanged. See
  [`../../src/Flare.Api/README.md`](../../src/Flare.Api/README.md#alerting)
  for what each channel type needs configured server-side.
- **Acknowledge or snooze.** A firing rule can be acknowledged or snoozed
  from the dashboard, the CLI, or the notification itself, which mutes
  repeats; unacknowledged alerts can escalate through further steps and
  on-call rotations (see
  [`../how-to/alerting/acknowledge-and-snooze-alerts.md`](../how-to/alerting/acknowledge-and-snooze-alerts.md)
  and [ADR-0124](../../docs-internal/adr/0124-alert-acknowledgement-and-snooze.md)).
- **Resolve.** When a firing rule recovers, its channels get a "Resolved"
  notification, and PagerDuty incidents close automatically. Each channel
  can opt out with its "Send resolved notifications" switch. The rule's
  history records the resolution next to the fire (see
  [ADR-0064](../../docs-internal/adr/0064-alert-resolved-notifications.md)).
- **Mute.** Each rule row has a Mute action (15 min to 1 week, or until a
  date, with an optional reason) that creates a one-off window scoped to that
  rule; the row shows "Muted until …" and an Unmute button ends it early.
  Settings > Workspace > Maintenance windows schedules one-off, daily or
  weekly windows (in a chosen time zone) for every rule, selected ones, or
  rules matching a label, so new rules are covered automatically. During a
  window, rules still evaluate, but a breach is recorded in the rule's
  history as suppressed instead of notifying (see
  [ADR-0055](../../docs-internal/adr/0055-alert-maintenance-windows.md)).
- **Summarize.** With an AI provider configured, a fired alert can get a
  plain-language summary (see
  [`../how-to/alerting/summarize-alerts-with-ai.md`](../how-to/alerting/summarize-alerts-with-ai.md)).

Alert rules can be exported and imported as JSON (or with `flare alerts
export` / `import`) to keep them in git. For the model behind all of this,
see [How alerting works](how-alerting-works.md).

![Alerts](../screenshots/alerts.png)
![New alert](../screenshots/alerts-en.webp)

### Views

`/views` — named, reloadable filters saved from the Logs, Traces, or
Metrics toolbar's "Views" control. Save a filter once (e.g. "Warnings
mentioning timeout"), and reload it from here or from that page's own
Views dropdown — shareable by link, not tied to whoever created it. Each
explorer also reopens on the last view you used.

![Views](../screenshots/views.png)

### Status pages

A status page (`/status/<slug>`) is a read-only page of service health that
anyone with the link can open, no account needed: each component's current
state and daily uptime, posted incidents, and optional email subscriptions,
under your own branding and domain. A component is a
[synthetic monitor](../how-to/monitor/synthetic-monitoring.md) (an HTTP, TCP,
TLS certificate, DNS, UDP or ICMP probe the alert worker runs on a
schedule, from one or more locations) or an
[SLO](../how-to/alerting/define-slos.md), shown under a public name you
choose. Pages are managed under Settings > Workspace. See
[`../how-to/monitor/publish-a-status-page.md`](../how-to/monitor/publish-a-status-page.md)
and [ADR-0158](../../docs-internal/adr/0158-status-pages.md).

### Data sources

Reached from the nav dropdown, or from the Logs page's empty state ("See
how to ingest data") right when someone actually needs it. A searchable,
copy-paste catalog of OTLP setup snippets organized by platform
(Kubernetes, Docker, Linux, Windows), language/SDK (.NET, Python,
Node.js, Java, Go), log shipper (Vector, Fluent Bit, Syslog), CI/DevOps
tool (Jenkins, Ansible, Terraform, GitHub Actions), and a raw HTTP/JSON
fallback for anything else — plus one Prometheus item filed under Metrics
for `Flare.Ingest`'s native scrape receiver, the one entry here that's
pulled rather than pushed. Every snippet's host/port comes from the
browser's own origin, so what you copy matches your actual deployment
instead of a placeholder — this is the same catalog the README's ".NET
only" snippets point to for every other language or platform.

![Data sources](../screenshots/catalog-en.webp)

### Settings

Configuration lives under **Settings** (`/settings`), a page with a left
rail and a search box. It has three groups:

- **Personal** — appearance (theme), language and display time zone,
  explorer defaults, keyboard shortcuts, browser alert notifications and your
  account. Open to everyone.
- **Management** — SLOs, pipeline rules (extract or redact fields at
  ingest), log-based metrics, and your personal access tokens.
- **Workspace** — instance-wide pages that need the Admin role (or auth
  switched off):

| Area | What it covers |
|---|---|
| Alerting | Notification channels, maintenance windows, shared alert templates, on-call rotations |
| Monitoring | Synthetic monitors, status pages |
| Data | Retention and cold storage, erasure and export of one person's data, telemetry export (OTLP forwarding and S3 archive), Usage |
| Indexing | The underlying ClickHouse store made visible: total storage (compressed/uncompressed), row counts, a table-by-table breakdown (`logs`, `spans`, `metrics_sum`, …) with compression ratios, growth over the last 30 days, and the skip indexes backing fast filtering. In cluster mode, this is also where the Cluster panel lives — see [`clustering.md`](clustering.md#dashboard-cluster-status-on-the-indexing-page) |
| Access | Projects (a team's allow-list of services), ingest keys (with origin and service allow-lists, limits and expiry), source maps, the audit log |

The Auth page (`/auth`) is where an admin turns authentication on and
chooses the methods: local accounts, Microsoft Entra ID, Active Directory
(LDAP), OpenID Connect, or trusted headers from a reverse proxy. See
[Authentication model](authentication-model.md) and
[Projects and access scoping](projects-and-access-scoping.md). Indexing,
retention and usage together answer "what is using the disk": see
[`../how-to/data/see-what-drives-storage.md`](../how-to/data/see-what-drives-storage.md)
and [Retention and tiering](retention-and-tiering.md).

![Indexing](../screenshots/indexing-en.webp)

![Indexing storage growth and tables](../screenshots/indexing-2-en.webp)

![Indexing query optimization](../screenshots/indexing-3-en.webp)

## Integrations and APIs

Flare is meant to sit next to the tools you already run, not replace all of
them at once:

- **AI assistants.** A read-only [MCP](../how-to/integrations/connect-ai-assistants.md)
  server exposes logs, traces, metrics and alerts to Claude Code, Cursor and
  VS Code, either over stdio with `flare mcp` or as a streamable-HTTP
  endpoint on `Flare.Api` (`/mcp`).
- **Grafana.** Flare's API answers read-only Prometheus
  ([ADR-0109](../../docs-internal/adr/0109-prometheus-query-api.md)),
  Tempo ([ADR-0217](../../docs-internal/adr/0217-tempo-compatible-trace-api.md))
  and Loki ([ADR-0236](../../docs-internal/adr/0236-loki-compatible-log-api.md))
  queries, so Grafana can use it as those data sources, including the
  Metrics, Traces and Logs Drilldown apps. A
  [Flare data source plugin](../how-to/integrations/query-with-grafana-plugin.md)
  adds sessions, errors and release health, and a dashboard can be exported
  as Grafana JSON.
- **Onward.** Flare can forward what it ingests to another OTLP endpoint
  ([ADR-0155](../../docs-internal/adr/0155-otlp-forwarding.md)) and archive
  it to S3-compatible storage as Parquet or NDJSON
  ([ADR-0156](../../docs-internal/adr/0156-telemetry-archive-s3.md)).
- **Configuration as code.** `flare config export` and `flare config apply`
  move rules and settings through git, and a Terraform/OpenTofu provider
  manages them declaratively
  ([ADR-0146](../../docs-internal/adr/0146-terraform-provider.md)).

## Why the CLI pins image tags instead of tracking `latest`

`Flare.Cli`-managed instances default to a specific, tested `vX.Y.Z` image
tag rather than the floating `edge`/`latest` tags (see
[the reference](../reference/cli-commands.md#image-tag-policy) for the
exact defaults and version history) — deliberately, so a given `Flare.Cli`
version keeps pulling the same images forever until you explicitly move it.
`flare update` (no `--tag`) re-pulls the same pinned tag rather than
auto-discovering newer releases, and deliberately never will: only this
CLI's own author knows which newer Flare Docker images have actually been
tested against a given `Flare.Cli` version — a newer tag existing on Docker
Hub isn't the same claim. Each new `Flare.Cli` release re-pins its own
template's default once tested against a newer Flare image; existing
installs keep tracking whatever tag they were generated with until you move
them explicitly with `flare update --tag TAG`.

## Why CLI-managed instances get random passwords

The repo's own `docker-compose.yml` ships a documented `flare`/`flare`
default password — fine for a stack you stand up, evaluate, and tear down.
A `Flare.Cli`-managed instance is meant to stand for weeks with its ports
bound on your machine the whole time, not be torn down after a quick eval,
so reusing a public, documented default password for something long-lived
is a foot-gun the CLI doesn't default into. Passwords are generated once at
first init and never rotated afterward (rotating would break
`identity-data`/ClickHouse auth on the next start) — the file is plain text
and yours to hand-edit if you'd rather set your own value.
