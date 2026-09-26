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
- **Multi-value dashboard variables.** A `DashboardVariable` resolves to
  one value or "All" (`defaultValue: string | null`); there's no way to
  scope a dashboard to e.g. two services at once. The underlying filters
  are already list-shaped (service lists etc.), so this is mostly a
  checkbox picker (with "only this" / "all" shortcuts) plus a
  `string[]` selection in the stored variable shape - and deciding how a
  multi-value parent narrows a chained child (ADR-0026). Not started.
  Prior art: [signoz#5191](https://github.com/SigNoz/signoz/commit/a65d5095a0dc1aadbf6b66bae665d25ebddc8bb2).
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
- **Create/invite additional local users.** With local auth,
  `/api/auth/bootstrap` creates only the first admin, and
  `UserEndpoints` can list users, change a role and disable a user, but not
  create one - there's no API or dashboard path to a second local account
  (live e2e runs have had to write users into SQLite directly). Needed: an
  admin-only "invite user" (email/username + role → one-time
  set-password link, expiring) plus a dashboard form on the users page;
  bulk invite is a nice-to-have. Not started. Prior art:
  [signoz#6057](https://github.com/SigNoz/signoz/commit/fc4b55cb34b48fd3f47719be6ad6008b42d7e77d).
