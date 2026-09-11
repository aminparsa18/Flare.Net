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
  throughput. Not started. Prior-art design worth reusing, from SigNoz's
  TTL/cold-storage implementation ([signoz#1173](https://github.com/SigNoz/signoz/commit/5d080f5564c7839d0908db48bc8fff47d0e55648)):
  cold storage isn't app-level archival, it's ClickHouse's own tiered
  storage — an S3-backed disk/volume defined in ClickHouse's own config,
  with `ALTER TABLE ... MODIFY TTL ... DELETE, ... TO VOLUME 'x'` moving
  aged parts onto it, so a table's storage policy just needs assigning
  once (idempotent) rather than anything bespoke on Flare's side; a
  `GetDisks`-style read of `system.disks` lets the retention UI offer a
  dropdown of volumes actually configured instead of free text. Because
  that `MODIFY TTL` is a long-running ClickHouse mutation, the set-TTL
  API should be async and status-tracked (a small table keyed by a
  transaction id, `pending`/`success`/`failed`, one row per underlying
  table) rather than blocking the request — reject a second set-TTL call
  while one's still `pending` instead of queuing another mutation, and
  have the GET endpoint return both the *actual* TTL (parsed live from
  ClickHouse) and the *expected* one (what was last requested) plus
  status, so the UI can show "applying…" instead of a stale value. One
  more ClickHouse config gotcha to get right when this is built: set
  `perform_ttl_move_on_insert: 0` on the S3 volume in ClickHouse's
  storage config - without it, ClickHouse evaluates the TTL-move rule
  synchronously on every insert once cold storage is configured, adding
  latency to the ingest path; the flag defers it to ClickHouse's
  background merge process instead
  ([signoz#1448](https://github.com/SigNoz/signoz/commit/f8f903848e914d529617c6e10c69b3644f8d4c30)).
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
- **LogQL attribute-map syntax.** The SQL query bar (`LogQlLexer`/
  `LogQlParser`/`LogQlAst`/`LogQlWhereTranslator` under
  [`src/Flare.Api/Query/LogQl/`](../../src/Flare.Api/Query/LogQl/)) only
  knows a fixed column list (`service`, `level`, `body`, `traceId`,
  `spanId`, `severityNumber`) - it has no syntax for reaching into the
  arbitrary key/value `LogAttributes`/`ResourceAttributes`/
  `ScopeAttributes` maps at all, unlike the structured `AttributeFilter`
  path (which now supports exists/absent/not-equals - see git history).
  A bigger change than that one: needs new grammar (e.g. `attributes.foo`),
  a new AST node, and translator support for `mapContains`/map-subscript
  SQL. Approach TBD (SQL-bar grammar vs. something else entirely) - not
  started.
- **Custom, user-built dashboards (multi-panel, saved, composed from
  arbitrary log/trace/metric queries).** A bigger item, likely needs its
  own design pass before implementation. Distinct from
  [`Saved views`](../../src/dashboard/src/routes/views) — a saved view is
  one named filter preset for a single Logs/Traces/Metrics page; a
  dashboard is a named collection of independent panels (mix of charts
  from different queries/signals) arranged on a grid. No CRUD for this
  shape exists anywhere in `Flare.Api` today. Not started. When this does
  get built: gate widget edit/delete and dashboard-description edit in
  the UI itself by role (admin/editor can mutate, viewer read-only), not
  just a 403 from the API — SigNoz does this at the button level
  (e.g. [signoz#1051](https://github.com/SigNoz/signoz/commit/5caf94f024c2447d04d7609c5e018ecd7cba1ed2),
  [#1066](https://github.com/SigNoz/signoz/commit/6c5a48082b0ea6eec51accf57de29ab1e611222b)).
  Also worth avoiding a pitfall SigNoz hit and walked back: don't wire
  grid-layout drag/resize directly to a persistence API call on every
  tick — they did, then removed it in favor of an explicit save
  ([signoz#1306](https://github.com/SigNoz/signoz/commit/63e663a92d88859f0ec5f5438ef9aba8641666ad)).
  Also worth designing in from the start rather than bolting on later:
  dashboard variables/templating - a saved variable backed by its own
  query (e.g. "distinct `service.name` values"), surfaced as a dropdown
  at the dashboard level, that every panel's query can reference
  ([signoz#1552](https://github.com/SigNoz/signoz/commit/461a15d52d2840cd6e50e237cd3f8ab9860321a7)).
  Lower-priority nice-to-have once dashboards exist: importing Grafana
  dashboard JSON, easing migration for anyone coming from Grafana
  ([signoz#1700](https://github.com/SigNoz/signoz/commit/9735a6e5c)).
  More design notes worth baking in from the start: variable chaining -
  one variable's choices narrow based on another's selected value
  ([signoz#2036](https://github.com/SigNoz/signoz/commit/cd9768c73),
  [#2037](https://github.com/SigNoz/signoz/commit/ca53136cb)); one global
  time range driving every panel, not per-panel pickers
  ([signoz#2013](https://github.com/SigNoz/signoz/commit/17f32e976));
  lazy-loading panels - only fetch/render what's in viewport, not every
  panel on page load ([signoz#2133](https://github.com/SigNoz/signoz/commit/af272a368));
  and exported dashboard JSON should carry only definitions, never
  embedded cached query results - SigNoz got this wrong first and fixed
  it later ([signoz#2052](https://github.com/SigNoz/signoz/commit/b72815ca2)).
- **Metric-threshold alerting, not just log-count alerting.**
  `AlertRule.Condition` ([`src/Flare.Api/Model/AlertModels.cs`](../../src/Flare.Api/Model/AlertModels.cs))
  is hard-typed to `LogFilter`, and `AlertThreshold.IsBreached` only ever
  compares an `observedCount` (rows matched by the filter) - there's no
  way to alert on e.g. "p99 latency > 500ms" or a metric gauge crossing
  a value, even though the query-side machinery a metric condition would
  need already exists (`MetricQueryService`/`MetricSeriesQueryBuilder`/
  `MetricFilter`). Needs a condition-kind discriminator on `AlertRule`
  (log-filter-count vs. metric-query-threshold) and
  `AlertEvaluationWorker`/`AlertQueryService` evaluating the metric
  branch through the existing `MetricQueryService` - not a from-scratch
  rule engine. SigNoz's own version of this
  ([signoz#1346](https://github.com/SigNoz/signoz/commit/3a287b2b169dfae093a656d10da5ab8e816b7d1f),
  [#1359](https://github.com/SigNoz/signoz/commit/a8c7237bb)) is a
  ~4700-line rewrite borrowing Prometheus's own rule-engine internals -
  worth knowing the gap exists and the shape of a condition, not worth
  copying that scale of machinery. Not started. Same root cause blocks
  exception-based alerting too: exceptions are queried through their own
  `ExceptionFilter`/`ExceptionFilterSqlBuilder` path over span-event data
  ([`src/Flare.Api/Query/ExceptionGroupQueryBuilder.cs`](../../src/Flare.Api/Query/ExceptionGroupQueryBuilder.cs)),
  entirely separate from `LogFilter`, so there's no way to alert on e.g.
  "this exception type occurred N times in 5 minutes" either. SigNoz has
  this as its own alert type ([signoz#1752](https://github.com/SigNoz/signoz/commit/33d34af2a)).
  Fold in as a third condition kind (exception-filter-based) alongside
  log-filter-count and metric-query-threshold, not a separate effort.
- **Reusable, named notification channels - a rule can only notify one
  destination today.** `AlertRule`'s `WebhookUrl`/`TelegramBotToken`+
  `TelegramChatId`/`EmailTo`/`PagerDutyRoutingKey`
  ([`src/Flare.Api/Model/AlertModels.cs`](../../src/Flare.Api/Model/AlertModels.cs))
  are mutually-exclusive inline fields on the rule itself - "a rule
  notifies exactly one channel," per its own doc comment. There's no
  saved/named channel entity: the same Slack webhook or PagerDuty
  routing key has to be re-entered on every rule that should reach it,
  rotating a key means updating every rule referencing it individually,
  and a single critical rule can't fan out to more than one destination
  (e.g. Slack *and* PagerDuty for the same breach). SigNoz models
  channels as their own managed, named objects a rule multi-selects
  ([signoz#1458](https://github.com/SigNoz/signoz/commit/7881aee3501c5081f020e61badd7c1607fc9946f)).
  Needs a new `NotificationChannel` entity (CRUD'd on its own page) and
  `AlertRule` referencing a set of channel IDs instead of embedding the
  destination fields directly - a bigger item, comparable in scope to
  the custom-dashboards one above. Not started.
- **"Go to trace by ID" quick-search in the Traces GUI page.** Low
  confidence, not fully verified - the terminal already has this via its
  `trace <id>` command
  ([`src/dashboard/src/lib/terminal/commands/trace.ts`](../../src/dashboard/src/lib/terminal/commands/trace.ts)),
  hitting the same `GET /api/traces/{traceId}` the GUI's trace-detail
  page uses, but no equivalent "paste a trace ID and jump to it" input
  was found on the GUI Traces page itself - only reachable there via
  navigating to `/traces/{traceId}` directly or filtering. SigNoz has a
  dedicated component for this
  ([signoz#1551](https://github.com/SigNoz/signoz/commit/eaadc3bb9)).
  Worth a closer look before committing to it. Not started.
- **No drag-to-zoom / brush-select on time-series charts.**
  [`MetricChart.svelte`](../../src/dashboard/src/lib/components/metrics/MetricChart.svelte)
  and [`VolumeChart.svelte`](../../src/dashboard/src/lib/components/logs/VolumeChart.svelte)
  both lack click-and-drag over the chart to zoom into that time range -
  `VolumeChart`'s own comment confirms a bar click only highlights
  rather than re-fetching a zoomed view, a deliberate choice worth
  revisiting. Common, expected charting UX for an observability tool.
  Reference: [signoz#2018](https://github.com/SigNoz/signoz/commit/1e39131c3).
  Not started.
- **GroupBy attribute-key picker has no search/autocomplete, unlike
  filter values.** [`MetricsToolbar.svelte:69-80`](../../src/dashboard/src/lib/components/metrics/MetricsToolbar.svelte)
  is a plain dropdown listing every attribute key, no type-to-filter.
  Flare already has autocomplete for attribute *filter values*; this is
  the same affordance missing on the *groupBy field* picker specifically
  - cheap, consistent extension of work already done. Reference:
  [signoz#2156](https://github.com/SigNoz/signoz/commit/02ef1744b).
  Not started.
