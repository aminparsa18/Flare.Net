# Architecture

> **This page is still growing.** It currently covers the three install
> paths, a tour of the dashboard, and CLI-specific rationale — surfaced by
> phases 4 and 6 of the documentation migration. Still missing: the
> ingest → buffer → ClickHouse → API pipeline diagram and Flare's stated
> design principles, which come from `Planning.md`'s own intro in a later
> phase. See
> [`../DOCUMENTATION-MIGRATION-PLAN.md`](../DOCUMENTATION-MIGRATION-PLAN.md).

## Three ways to run Flare, and why each exists

Flare has three legitimate install paths, each solving a different problem
rather than being redundant with the others:

- **[.NET Aspire](../how-to/run-with-aspire.md)** (`Flare.Hosting.Aspire`) —
  for an app that already has an AppHost. Flare joins the resource graph;
  `aspire start` already orchestrates its lifecycle alongside everything
  else.
- **[Standalone Docker Compose](../how-to/run-standalone.md)** — for a
  one-off, repo-local evaluation. `docker compose up` at the repo root is
  the fastest way to just look at Flare once.
- **[The `flare` CLI](../how-to/run-with-cli.md)** (`Flare.Cli`) — for a
  standing instance you start once and forget about, from any directory,
  shared across many unrelated local projects, independent of any single
  AppHost's lifecycle. This is the case the other two paths structurally
  can't cover: Aspire mode ties Flare's lifecycle to one AppHost, and a
  repo-local Compose stack isn't meant to run for weeks in the background
  serving unrelated projects.

## Tour of the dashboard

Whichever install path you pick, you land in the same place:
`Flare.Dashboard` — a single SvelteKit SPA with seven pages behind one nav
bar, all talking to `Flare.Api` over HTTP/WebSocket, no separate tools for
logs, traces, metrics, or alerting. First visit creates the admin account
(see [`../how-to/configure-authentication.md`](../how-to/configure-authentication.md));
after that it's a normal login.

### Logs

The default view (`/`). A dense, virtualized log table with live tail
(real-time streaming, pause/resume), an event-volume chart, and filters
for service, level, and free-text search over the message body. Click any
row to expand its full structured payload, scopes, and exception details.

![Logs Explorer](../screenshots/logs.png)

### Traces

`/traces` — every OTLP trace Flare has received, filterable by service and
time range. Auto-instrumented spans (ASP.NET Core, HttpClient, …) and
anything your own code emits via `ActivitySource` show up side by side.

![Traces list](../screenshots/traces.png)

Click into a trace for the waterfall view — parent/child spans laid out by
start time and duration, the same shape as Jaeger/Zipkin but wired
straight into the rest of the dashboard.

![Trace detail waterfall](../screenshots/trace-detail.png)

### Metrics

`/metrics` — every OTLP metric instrument (Sum, Gauge, Histogram) reported
by your services, browsable from a searchable sidebar and rendered as a
time-series chart per instrument. Covers both the free
`AddAspNetCoreInstrumentation()`/`AddRuntimeInstrumentation()` data (.NET
GC, thread pool, Kestrel, HTTP client/server) and anything your own
`Meter` emits.

![Metrics browser](../screenshots/metrics.png)

### Ingestion

`/ingestion` — operational visibility into the OTLP receiver itself:
current arrival/ingestion rates, a per-signal (logs/traces/metrics) ×
per-protocol (gRPC/HTTP) breakdown of requests, events, and bytes, plus an
ingestion log of anything Flare rejected (bad payloads, unsupported media
types) and why. The page to check first if "my logs aren't showing up."

![Ingestion](../screenshots/ingestion.png)

### Indexing

`/indexing` — the underlying ClickHouse store made visible: total storage
(compressed/uncompressed), row counts, table-by-table breakdown (`logs`,
`spans`, `metrics_sum`, `metrics_histogram`, `metrics_gauge`, …) with
compression ratios, growth over the last 30 days, and the skip indexes
backing fast filtering. Useful for capacity planning or just seeing where
the bytes go. In cluster mode, this is also where the Cluster panel lives
— see [`clustering.md`](clustering.md#dashboard-cluster-status-on-the-indexing-page).

![Indexing](../screenshots/indexing.png)

### Alerts

`/alerts` — threshold/query-based alert rules: a saved filter (service,
level, search text) plus a count threshold evaluated on a rolling window,
with a cooldown and a "test against current data" dry-run before saving.
Firing notifies one of webhook/Slack, Telegram, or email, depending on the
rule's "Notify via" channel — see
[`../../src/Flare.Api/README.md`](../../src/Flare.Api/README.md#alerting)
for what each channel needs configured server-side.

![Alerts](../screenshots/alerts.png)
![Alerts](../screenshots/alerts_add.png)

### Views

`/views` — named, reloadable filters saved from the Logs, Traces, or
Metrics toolbar's "Views" control. Save a filter once (e.g. "Warnings
mentioning timeout"), and reload it from here or from that page's own
Views dropdown — shareable by link, not tied to whoever created it.

![Views](../screenshots/views.png)

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