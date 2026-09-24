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
- **Scoped "fired data" alert links for `MetricThreshold`/`ExceptionCount`
  rules.** `LogCount` rules already link into `/?state=` (see
  `AlertMessageFormatter.BuildMatchingLogsUrl`); the other two kinds still
  only get the `/alerts?rule=` link, because `/metrics` and `/errors` don't
  yet restore a filter + custom range from the URL.
- **OTel `ExponentialHistogram` metric support.** Confirmed deliberately
  unsupported today - `MetricPointRecord`'s own remarks say
  ExponentialHistogram/Summary points are recognized on the wire and
  dropped, "no feature in this roadmap slice consumes them, [use the]
  add it when a concrete need exists precedent" (the same precedent
  Span Links followed before it later got built). .NET's OpenTelemetry
  SDK commonly emits exponential histograms (`Base2ExponentialBucketHistogram`
  is a standard `Meter` aggregation choice), so real .NET metrics can
  silently vanish from ingest today. Not started - would need a new
  `metrics_exponential_histogram`-shaped additive ClickHouse table (own
  bucket/scale representation, distinct from the existing explicit-bucket
  `metrics_histogram`) plus a DDSketch-style or scale-aware quantile
  merge distinct from `HistogramQuantileEstimator`. Prior art: SigNoz's
  exponential-histogram table + quantile merge
  ([signoz#4525](https://github.com/SigNoz/signoz/commit/f734142419e928151a0f021d9febf7a2e6db5621)).
- **Dashboard panel visualization types.** `PanelType` is only the data
  source (`Logs`/`Traces`/`Metrics`), and a Metrics panel always renders
  as a line chart - no bar, pie, single-value stat, or table rendering,
  and no way to switch an existing panel's visualization in place
  (keeping its query). Mostly frontend: a per-panel `visualization` field
  alongside `panelType` in the dashboard's stored panel JSON. Not started.
  Prior art: pie chart panel [signoz#4751](https://github.com/SigNoz/signoz/commit/a54b7baa7d4754fb752cc61a048f2f8ff167241c),
  change panel type in place [signoz#4759](https://github.com/SigNoz/signoz/commit/6815a96d29e1c6ca0059621bf56b2949f7af378a).
  Related per-visualization options worth folding in when built: value
  histogram [signoz#4858](https://github.com/SigNoz/signoz/commit/7e9bf2d48da640b7203e4cd19cdf91575dedfde2),
  stacked bars [signoz#5138](https://github.com/SigNoz/signoz/commit/f2aba5035a2f106be45848e5eee9e012da6ed5f4),
  and for the table visualization: CSV download [signoz#5067](https://github.com/SigNoz/signoz/commit/76b1e40cbc2182165abbb538f32481265bd35b75),
  per-column unit [signoz#5134](https://github.com/SigNoz/signoz/commit/2145e353c81ab22ef60b09e4f71b8917a3f16709),
  click-to-sort columns [signoz#5114](https://github.com/SigNoz/signoz/commit/0760917a4b54bf6629a5c08d02201407797d00bf),
  in-table search [signoz#5893](https://github.com/SigNoz/signoz/commit/cb1cd3555b3b63bdb441512dacdebf2599db67d7);
  and units on pie-chart values [signoz#5960](https://github.com/SigNoz/signoz/commit/3573c0863c59711d48b28d91d4d775dbc4929666).
- **Collapsible rows / panel groups on dashboards.** Dashboards are one
  flat gridstack grid; a named, collapsible row that owns the panels
  beneath it would keep large dashboards navigable (and, collapsed, skip
  querying those panels - pairs naturally with the existing lazy-load-on-
  scroll gating). Frontend-only apart from the stored layout shape. Not
  started. A collapsed row should show how many panels it holds. Prior
  art: [signoz#4806](https://github.com/SigNoz/signoz/commit/191d9b0648bc084cf0d4adbfc00ca1238721cf90),
  panel count on collapsed rows [signoz#5822](https://github.com/SigNoz/signoz/commit/afc97511af366bb6f78408a30c420ade573b6610).
- **Planned maintenance windows (alert silencing).** No way to mute
  notifications today - a deploy or planned downtime pages everyone. A
  maintenance window = a set of alert rules (or all) + a one-off or
  recurring time range during which `Flare.AlertWorker` still evaluates
  but suppresses notifications (recording the suppressed firing in alert
  history rather than dropping it silently). Needs an additive ClickHouse
  table + a dashboard page. Not started. Prior art:
  [signoz#4863](https://github.com/SigNoz/signoz/commit/7e79900973da430179292ffc865ad44908763035).
- **Facet filter sidebar on the Traces and Logs pages.** Both only have
  the toolbar filter row; a collapsible sidebar listing values per facet
  (Traces: service, status, operation, duration buckets, chosen span
  attributes; Logs: service, severity, environment, host, chosen log
  attributes) with counts and click-to-filter would make exploration much
  faster, reusing the existing span/log attribute-values endpoints for the
  lists. One shared sidebar component. Mostly frontend. Not started.
  Prior art: Traces [signoz#5081](https://github.com/SigNoz/signoz/commit/9733612be8a90ec6bcf8c48ed16df96f1073b0bd),
  Logs [signoz#5799](https://github.com/SigNoz/signoz/commit/4a9847abdd4cc02d0bac89c1215200203a2133d9).
- **Multi-value dashboard variables.** A `DashboardVariable` resolves to
  one value or "All" (`defaultValue: string | null`); there's no way to
  scope a dashboard to e.g. two services at once. The underlying filters
  are already list-shaped (service lists etc.), so this is mostly a
  checkbox picker (with "only this" / "all" shortcuts) plus a
  `string[]` selection in the stored variable shape - and deciding how a
  multi-value parent narrows a chained child (ADR-0026). Not started.
  Prior art: [signoz#5191](https://github.com/SigNoz/signoz/commit/a65d5095a0dc1aadbf6b66bae665d25ebddc8bb2).
- **User-chosen aggregation interval (bucket width / step).** The Logs
  volume chart auto-picks its bucket width (`lib/logs/bucket-width.ts`)
  and Metrics has no step control at all; a manual override (e.g. force
  1m buckets over 24h, or 1h for a smoother trend), carried in saved
  searches/dashboard panels, would help both. `/api/logs/aggregate`
  already accepts `bucketWidthSeconds`. Not started. Prior art:
  [signoz#5074](https://github.com/SigNoz/signoz/commit/dc294ff6d57c3940cba03e07410e22135cd0c2d4).
- **Research: does the Logs free-text search actually use `idx_body`?**
  `LogFilterSqlBuilder` compiles `Search` to `Body ILIKE '%…%'`, but
  `idx_body` (`db/clickhouse/0001_logs.sql`) is a `tokenbf_v1` index, and
  ClickHouse's bloom-filter skip indexes aren't documented as usable for
  `ILIKE` - so every search may be full-scanning `Body` within the time
  window, and pattern (e) in
  [`../investigations/benchmark-ingest-and-query.md`](../investigations/benchmark-ingest-and-query.md)
  may be measuring a scan, not the index. First step: `EXPLAIN indexes = 1`
  on a real search against a live ClickHouse. If confirmed, likely fix is
  an additive `ngrambf_v1` index on `lower(Body)` with the search rewritten
  as `lower(Body) LIKE lower(…)` (new migration + probably an ADR). Prior
  art: [signoz#4787](https://github.com/SigNoz/signoz/commit/1585065fff9b7853d63e64abebf2887ecc42cc72).
- **Messaging-queue (Kafka etc.) monitoring.** Per-topic/partition producer
  and consumer throughput, latency and consumer lag, derived from spans'
  OTel `messaging.*` semantic-convention attributes (plus Kafka consumer-lag
  metrics where a collector exports them). Nothing in Flare covers this
  today. Large - its own page and probably its own ADR. Not started. Prior
  art: [signoz 481bb6e](https://github.com/SigNoz/signoz/commit/481bb6e8b8d68b40d5b6706b91bb78b71d59a3c7).
- **Group the Logs volume chart by an attribute.** The Logs explorer's
  `VolumeChart` renders a single ungrouped series, and `/api/logs/aggregate`'s
  `LogAggregateGroupBy` only knows `None`/`Service`/`Level`. Needs an
  attribute group-by (bag + key) in `LogAggregateRequest`/`LogAggregateQueryBuilder`
  plus stacked-series rendering in `VolumeChart`, then a "group by" action on
  `AttributeTable` rows next to the existing filter-for/out/copy actions.
  Not started. Prior art: group by from log details [signoz#5753](https://github.com/SigNoz/signoz/commit/ab1caf13fc201bf99e0317c2d5f445f485882b10).
- **`runningDiff` (point-to-point delta) metric function.** Change between
  consecutive points of a series, e.g. for gauges where the step change
  matters more than the level. Likely a new function in the Metrics
  Formula mode (ADR-0036) rather than a ClickHouse `runningDifference`
  (which misbehaves across block boundaries). Not started. Prior art:
  [signoz#5667](https://github.com/SigNoz/signoz/commit/4489df6f395fa6ba4e2cbc79011f10fb6c76e628).
- **More metric-alert match types.** A Gauge `MetricThreshold` rule always
  compares the window's *average* (`MetricAlertAggregation.Value` =
  `avg(Value)`); there's no "last value", "max"/"min", or "every point in
  the window breached" ("all the time"). So "disk free below 5% right
  now" or "CPU over 90% for the whole 10 minutes" can't be expressed
  precisely. New `MetricAlertAggregation` members (additive enum, same
  shape as ADR-0020/0044) + the worker's query + the form's picker. Not
  started. Prior art: "last" match type
  [signoz#5929](https://github.com/SigNoz/signoz/commit/4edc6dbeae834162a64ddce2f575f4d61244f913).
- **Restore the last-used saved search per explorer.** Reopening Logs /
  Traces / Metrics starts from defaults even if the user was working in a
  saved search; remember the last one per page (per-browser, try/catch'd
  localStorage, self-healing if it was deleted - same pattern as the
  dashboards "set as home page" toggle). Frontend-only. Not started.
  Prior art: [signoz#5453](https://github.com/SigNoz/signoz/commit/3c151e3adbb2f051f43959594a411658d4cb8c7f).
- **Host/pod metrics tab in the log event detail view.** When a log
  carries `host.name` / `k8s.pod.name`, show that host's or pod's CPU and
  memory charts for a window around the log's timestamp, reusing the
  existing metrics queries and the Resources page's host data - "was the
  box starved when this error happened" without leaving the log.
  Frontend-only. Not started. Prior art:
  [signoz#5771](https://github.com/SigNoz/signoz/commit/c5b5bfe5406d2dc3c59f50977b76fa4d53d7bc23).
- **Create/invite additional local users.** With local auth,
  `/api/auth/bootstrap` creates only the first admin, and
  `UserEndpoints` can list users, change a role and disable a user, but not
  create one - there's no API or dashboard path to a second local account
  (live e2e runs have had to write users into SQLite directly). Needed: an
  admin-only "invite user" (email/username + role → one-time
  set-password link, expiring) plus a dashboard form on the users page;
  bulk invite is a nice-to-have. Not started. Prior art:
  [signoz#6057](https://github.com/SigNoz/signoz/commit/fc4b55cb34b48fd3f47719be6ad6008b42d7e77d).
- **Anomaly-detection alerts.** A fourth `AlertConditionKind` that fires
  when a series deviates from its own seasonal baseline (z-score against
  the same window a day/week earlier) instead of crossing a fixed
  threshold - catches "traffic is half what it normally is at this hour".
  Large; needs an ADR (baseline window, seasonality, minimum history).
  Not started. Prior art: rule [signoz#5973](https://github.com/SigNoz/signoz/commit/419d2da363dd01f66b5c24f0e78ff863605b9a2a),
  UI [signoz#5916](https://github.com/SigNoz/signoz/commit/21801180949459200d5b471d822e5ad08a75215f).
- **Minimum data points for alert evaluation.** An optional per-rule
  "require at least N points in the window" so a `MetricThreshold`
  average over one or two sparse samples doesn't fire (treated as
  "insufficient data", not breach). Pairs with the absent-data item
  above. Not started. Prior art:
  [signoz#5242](https://github.com/SigNoz/signoz/commit/4f76e13dbe6a62e394a1b3c583cd1c1d8826af0f).
- **Per-panel descriptions on dashboards.** Dashboards have a
  description, panels don't; an optional panel description shown via an
  info icon next to the title. Frontend-only (stored panel JSON). Not
  started. Prior art:
  [signoz#6133](https://github.com/SigNoz/signoz/commit/440fd4e02b2d8c13d8b4827abdfe609755cba990).
- **Expand/collapse all in the trace waterfall.** `TraceWaterfall` has no
  expand-all/collapse-all control. Frontend-only, tiny. Not started.
  Prior art: [signoz#5980](https://github.com/SigNoz/signoz/commit/266ed58908402553898cd7571c03d9810a02ba2a).
- **Keep the dashboard out of search indexes.** No `noindex` meta tag or
  `robots.txt` in `src/dashboard` - an internet-exposed Flare can get
  indexed. Add `<meta name="robots" content="noindex, nofollow">` in
  `app.html` and a deny-all `static/robots.txt`. Tiny. Not started. Prior
  art: [signoz#5793](https://github.com/SigNoz/signoz/commit/88ace79a644a12a3b32684c524ec81eca1cb137f).
- **Filter logs by instrumentation scope (the .NET logger category).**
  The OTel .NET SDK maps an `ILogger<T>` category (e.g.
  `MyApp.Orders.OrderService`) to the log's instrumentation scope name;
  Flare already stores and returns it (`LogEventDto.ScopeName`) but
  `LogFilter` can't filter on it - no "only `OrderService`" or "everything
  under `Microsoft.EntityFrameworkCore.*`". Add a `ScopeNames` filter
  (exact + prefix match) to `LogFilter`/`LogFilterSqlBuilder`, a skip
  index if needed, and a filter control; also a natural facet for the
  Logs facet sidebar item above. Not started. Prior art:
  [signoz#6378](https://github.com/SigNoz/signoz/commit/471803115ed91b1cf6404e4bb02cf3745925fea4).
- **Custom alert notification templates.** Notification text is fixed by
  `AlertMessageFormatter`; allow an optional per-rule title/body template
  with placeholders (rule name, observed value, threshold, window, group
  labels incl. dotted keys like `service.name`, deep links) - a small,
  safe placeholder substitution, not a general template engine - plus a
  live preview in the rule form reusing the send-test path. Not started.
  Prior art: [signoz#6282](https://github.com/SigNoz/signoz/commit/68d25a89894e141f208b62ca3c2c220a37cbc013).
- **Decide: should errored requests count as Apdex "frustrated"?**
  `ServiceApdexQueryBuilder` classifies satisfied/tolerating purely by
  `DurationNano`, so a request that fails in 5ms counts as satisfied and
  a service returning fast 500s can show near-perfect Apdex. The original
  Apdex spec is duration-only, but most APM tools (e.g. New Relic) treat
  errors as frustrated; [ADR-0032](../adr/0032-apdex-score-per-service.md)
  never addresses it. If adopted: add `StatusCode != Error` to both
  `countIf`s + tests, and record it in a new ADR superseding that part
  of 0032 (user-visible scoring change). Found while checking
  [signoz#6460](https://github.com/SigNoz/signoz/commit/c93cf1ce9515526eb6a667298fec56d3a068ee69)
  (an unrelated tolerating-halving fix Flare already gets right).
- **Host list from ingested OTel host metrics.** The Resources page is fed
  by Flare's own Docker/K8s/host pollers; a host that only ships
  `hostmetrics`-receiver metrics (`system.cpu.*`, `system.memory.*`,
  `system.filesystem.*`) over OTLP appears nowhere. A host inventory
  table (one row per `host.name`: CPU, memory, disk, last seen) with
  filters and drill-down into its metrics, queried from the existing
  metrics tables. Not started. Prior art:
  [signoz#6210](https://github.com/SigNoz/signoz/commit/e4505693b0f88c6e404dd8aaa0efb959259b6ea4).
- **Per-ingest-key ingestion limits.** `Flare.Ingest` authenticates with
  ingest API keys but has no rate or volume cap, so one noisy app can
  flood Redis Streams/ClickHouse for everyone. Optional per-key limits
  (events or bytes per minute/day, enable toggle) enforced with Redis
  counters at the OTLP receiver, rejecting with the OTLP-standard
  retryable status when exceeded, plus current usage shown on the ingest
  keys page. Not started. Prior art:
  [signoz#6430](https://github.com/SigNoz/signoz/commit/504bc0d541210c6fbe9e0a6509c10febf134a217).
- **Create a notification channel from inside the alert rule form.**
  Today the form only shows a hint when no channels exist; offer a
  "create channel" action (opening `NotificationChannelFormDialog`) and
  re-fetch the channel list when the picker opens. Frontend-only, small.
  Not started. Prior art:
  [signoz#6416](https://github.com/SigNoz/signoz/commit/a59e7b9dfb107b79b181735a9b5ec48ffb82f0f3).
