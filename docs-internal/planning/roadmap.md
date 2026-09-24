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
- **Absent-data ("no data") alerting.** No `AlertConditionKind` fires when a
  rule's query returns nothing at all for N minutes - a dead exporter or a
  service that stopped emitting is silent today, since every existing kind
  only compares a present value against a threshold. Likely an opt-in
  per-rule flag + window rather than a fourth condition kind. Not started.
  Prior art: [signoz#3245](https://github.com/SigNoz/signoz/commit/3c419677e1cb266e483d078692d05f02bbffcac2).
- **Per-rule alert evaluation frequency.** `Flare.AlertWorker` evaluates
  every enabled rule on the one global `AlertingOptions.PollInterval`
  (30s); a per-rule "evaluate every" (e.g. 1m/5m/15m for slow, expensive
  rules) would need a last-evaluated timestamp per rule so the worker can
  skip rules not yet due. Not started. Prior art:
  [signoz#4697](https://github.com/SigNoz/signoz/commit/83f68f13db3dbedf692f7d2b0eb25c4ea99410cb).
- **Pin attributes in the log event detail view.** Let a user pin chosen
  attribute keys so they render first in `EventDetailSheet` for every
  event (per-browser preference). Frontend-only. Not started. Prior art:
  [signoz#4692](https://github.com/SigNoz/signoz/commit/9f30bba9a8e50f2403a2f7503f29bd2b01f172f7).
- **Configurable lines per row in the Logs table.** `LogTable` has no
  density/line-count option (1 line / N lines / full wrap for the body
  column). Frontend-only, likely carried in a saved search like other
  display preferences. Not started. Prior art:
  [signoz#4737](https://github.com/SigNoz/signoz/commit/ae0d685b29d0a05c96f92ae66c7e0bf976e97e71).
- **`has` / `not has` operators for JSON-array body filters.** The body-JSON
  filter (`LogFilter`'s `JSONHas`/`JSONExtractString` path) can't express
  "array at this path contains value" (ClickHouse `has(JSONExtractArrayRaw(...), ...)`).
  Not started. Prior art:
  [signoz#4736](https://github.com/SigNoz/signoz/commit/9e557a0ebe526ab04d6e389a49f45f3f10976d06).
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
  click-to-sort columns [signoz#5114](https://github.com/SigNoz/signoz/commit/0760917a4b54bf6629a5c08d02201407797d00bf).
- **Collapsible rows / panel groups on dashboards.** Dashboards are one
  flat gridstack grid; a named, collapsible row that owns the panels
  beneath it would keep large dashboards navigable (and, collapsed, skip
  querying those panels - pairs naturally with the existing lazy-load-on-
  scroll gating). Frontend-only apart from the stored layout shape. Not
  started. Prior art: [signoz#4806](https://github.com/SigNoz/signoz/commit/191d9b0648bc084cf0d4adbfc00ca1238721cf90).
- **Planned maintenance windows (alert silencing).** No way to mute
  notifications today - a deploy or planned downtime pages everyone. A
  maintenance window = a set of alert rules (or all) + a one-off or
  recurring time range during which `Flare.AlertWorker` still evaluates
  but suppresses notifications (recording the suppressed firing in alert
  history rather than dropping it silently). Needs an additive ClickHouse
  table + a dashboard page. Not started. Prior art:
  [signoz#4863](https://github.com/SigNoz/signoz/commit/7e79900973da430179292ffc865ad44908763035).
- **Facet filter sidebar on the Traces page.** Traces only has the toolbar
  filter row; a collapsible sidebar listing values per facet (service,
  status, operation, duration buckets, chosen span attributes) with
  counts and click-to-filter would make exploration much faster, reusing
  the existing span-attribute-values endpoint for the lists. Mostly
  frontend. Not started. Prior art:
  [signoz#5081](https://github.com/SigNoz/signoz/commit/9733612be8a90ec6bcf8c48ed16df96f1073b0bd).
