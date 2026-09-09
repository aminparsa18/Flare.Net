# Roadmap

Forward-looking, still-open items only — no diary of what's already
shipped. See [`../README.md`](../README.md) for the rule this file exists
to enforce (a completed item is deleted here the same PR that ships it,
not checked off and kept); `git log` and the `adr`/`investigations`
folders are where "what happened and why" actually lives.

- **Retention policies + cold storage to S3-compatible object storage
  (RustFS).** A separate item from multi-node scaling (which shipped —
  see [`../adr/0003-distributed-tables-plain-names-and-sharding.md`](../adr/0003-distributed-tables-plain-names-and-sharding.md)
  and [`../../docs/explanation/clustering.md`](../../docs/explanation/clustering.md)):
  this one is retention/cold storage, not horizontal availability/
  throughput. Not started.
- **Research: a real "skip-index effectiveness" signal for the Indexing
  page.** Deliberately not shipped — ClickHouse doesn't expose this as
  reliable production telemetry today. Full findings, including upstream
  ClickHouse's own attempt at exactly this (merged then reverted for a
  correctness bug) and what to check before revisiting:
  [`../investigations/skip-index-effectiveness-signal.md`](../investigations/skip-index-effectiveness-signal.md).
  Until upstream lands something reliable, the fallback is a
  differently-labeled, genuinely-computable proxy (e.g. "% of queries
  reading under N% of their table's total rows" from `system.query_log`) —
  real, just not skip-index-specific, since primary-key pruning contributes
  too.
- **Per-service RED-metrics overview ("Services" landing page).** A
  sortable table aggregating trace spans into per-service request rate,
  error rate, and p99 latency — so "which service is unhealthy right now"
  is answerable at a glance. Today that answer only comes from the raw
  Traces search explorer or the generic Metrics picker/chart, neither of
  which aggregates by service. Not started.
- **Zero-prerequisite quick-install script for the docker-compose path.**
  A `curl | bash` installer (OS/package-manager detection, installs Docker
  if missing, pulls and starts the standalone stack) for evaluators with
  neither the repo cloned nor a .NET SDK — a fourth, even-lower-friction
  entry point alongside the existing three install paths (Aspire
  integration, `docker compose up`, the `flare` CLI). Not started.
- **Global service map + per-service dependency breakdown (external calls,
  DB operations).** Aggregates spans across *all* traces in a time window
  into a dependency graph (nodes/edges colored by error rate and latency),
  plus per-service "what am I calling, and how slow/erroring is each one"
  tabs grouped by `peer.service` (external HTTP calls) and `db.system`/
  `db.operation` (DB calls). Flare already has the per-trace building
  blocks (`$lib/traces/service-map.ts`, `ServiceMap.svelte`, `peer.service`
  handling) but scoped to one trace's spans only ("the trace as a journey
  through your architecture") — this item is aggregating that same shape
  across traces and over time, plus adding the external-call/DB-operation
  grouping the trace/span query layer doesn't do yet. Same underlying
  span-attribute work as the RED-metrics overview item above; natural to
  build together. Not started.
- **Attribute filter operators beyond equality, for logs and spans both:
  "exists"/"absent" and "exclude"/negate.** `AttributeFilter`/
  `SpanAttributeFilter`
  ([`LogFilter.cs`](../../src/Flare.Api/Model/LogFilter.cs),
  [`SpanFilter.cs`](../../src/Flare.Api/Model/SpanFilter.cs)) only support
  exact-value equality on an attribute key today. Two related gaps, same
  underlying filter-shape work: no way to ask "show me records where this
  attribute is/isn't set at all" (e.g. spans missing `tenant.id`, logs with
  `error.stack` present), and no way to exclude/negate a value (`!=`
  instead of `=`). `Kinds`/`StatusCodes` on `SpanFilter` are the existing
  precedent for a non-equality match shape to follow. Not started.
- **Custom, user-built dashboards (multi-panel, saved, composed from
  arbitrary log/trace/metric queries).** A bigger item, likely needs its
  own design pass before implementation. Distinct from
  [`Saved views`](../../src/dashboard/src/routes/views) — a saved view is
  one named filter preset for a single Logs/Traces/Metrics page; a
  dashboard is a named collection of independent panels (mix of charts
  from different queries/signals) arranged on a grid. No CRUD for this
  shape exists anywhere in `Flare.Api` today. Not started.
- **Auto-refresh toggle for the Traces and Metrics explorer pages.**
  Re-runs the current query on an interval (e.g. every 30s) so the page
  stays live without a manual re-search. Logs already has live-tail via
  WebSocket, a different mechanism that doesn't cover Traces or Metrics —
  neither `$lib/traces` nor `$lib/metrics` implements interval-based
  polling today. Small, self-contained; no backend changes needed. Not
  started.
- **Exceptions/errors tracking page.** Groups recorded exceptions by
  type/message across services — occurrence count, first/last seen,
  affected services — distinct from the RED-metrics overview item above
  (that's aggregate *rate*; this is *which specific exceptions*, à la
  Sentry). The raw material already exists (`SpanRecord.Events`/
  `SpanEvent` capture OTel span events generically, which is where
  `exception.type`/`exception.message`/`exception.stacktrace` live as
  event attributes), but nothing groups/dedupes it and there's no
  `routes/errors` page. Not started.
- **Attribute-value autocomplete in the Logs/Traces filter builders.**
  Given a key (e.g. `http.route`), suggest actual observed values instead
  of requiring an exact typed match. Today only service names get this
  treatment (`loadKnownServices()` in `$lib/traces/state.svelte.ts`) — no
  general "suggest values for this attribute key" endpoint or UI exists
  for either signal. Not started.
- **Roll up "does this trace contain any error" across all its spans, not
  just the root span.** `/api/spans/search` with `RootSpansOnly` returns
  one row per trace via its root span, and `TraceRow.svelte`'s status
  badge reflects only that root span's `statusCode` — a trace with a
  healthy root span (e.g. gateway returns 200) but an erroring span deeper
  in the call chain currently shows as healthy in the trace list, only
  visible once the waterfall is opened. Needs a server-side rolled-up
  flag, not a client-side fix. Not started.
- **Notification channel improvements: PagerDuty as a fourth channel type,
  plus a "send test alert" action on any channel.**
  [`AlertModels.cs`](../../src/Flare.Api/Model/AlertModels.cs) supports
  exactly three mutually-exclusive channels today (webhook/Slack via
  `WebhookUrl`, Telegram, Email) via `ValidateChannel()` — no PagerDuty,
  and no way to verify a channel's config (URL, bot token, SMTP address)
  actually works before relying on it in a real incident. PagerDuty would
  follow the same one-channel-per-rule shape as the existing three; the
  test action is channel-agnostic and applies to all four. Not started.
