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
- **Service RED metrics pre-aggregated at flush time, not queried live.**
  `ServiceOverviewQueryBuilder`/`ServiceCallBreakdownQueryBuilder` currently
  `GROUP BY ServiceName` over the raw `spans` table on every Services-tab
  load. Not started. Same shape as an already-shipped Flare pattern -
  Drain log-pattern clustering computed once at ingest/flush time rather
  than recomputed per query (ADR-0007) - so this would add an additive
  `service_metrics` ClickHouse table populated by `SpanFlushWorker`
  alongside the existing span write, and repoint the Services tab/
  dependency graph at it instead of re-scanning spans. Prior art: SigNoz's
  `USE_SPAN_METRIC` feature flag sourcing the same page from
  collector-generated span metrics instead of live trace aggregation
  ([signoz#3134](https://github.com/SigNoz/signoz/commit/433f930956db03c01bcbd72ea61e43bce1eddc57),
  [signoz#3188](https://github.com/SigNoz/signoz/commit/bc4a4edc7f8ac5d2b1bbcca642927df1cc37c9af),
  [signoz#3196](https://github.com/SigNoz/signoz/commit/562621a1171a5cbdb7d11a4e24f0c8fe2199b1da)).
- **Apdex score per service.** No Apdex support today. Not started. Would
  need a small per-service threshold (T value) setting - alongside
  Identity's SQLite, since it's per-installation config, not telemetry -
  plus a ClickHouse aggregation over existing span durations to compute
  the standard satisfied/tolerating/frustrated ratio for the Services
  tab. Prior art: SigNoz's user-configurable per-service Apdex threshold
  ([signoz#3186](https://github.com/SigNoz/signoz/commit/cac637ac88a2672c2df647c8a1c7308cbe90daf5)).
- **User-defined field extraction/redaction at ingest.** `Flare.Ingest`
  only does Drain pattern clustering at flush time today - no
  user-configurable regex/JSON field extraction or redaction on log
  bodies/attributes before they hit ClickHouse. Not started; scope TBD.
  SigNoz's version of this ([signoz#2457](https://github.com/SigNoz/signoz/commit/1a3e46cecd0bc5bd7e32d3830d81e833f59c7be3),
  [signoz#3185](https://github.com/SigNoz/signoz/commit/2cdafa0564c58499e939dddac404679d912ce6cc))
  is EE-only and pushes pipeline config out to remote OTel-collector
  fleets - that doesn't map onto Flare's single self-hosted ingest with
  no fleet management, so the narrower version worth considering is rules
  applied locally in `Flare.Ingest` at the same flush-time seam as Drain
  clustering, not a collector-config-push mechanism. Implementation
  detail worth reusing when this is built: route which rule applies to
  an event using Flare's existing `LogFilter` shape as the condition,
  the same way it's already reused verbatim for search and alert rules,
  rather than inventing a fourth filter shape - mirrors SigNoz's own
  later move to reuse its query-builder filter type for pipeline routing
  ([signoz#3560](https://github.com/SigNoz/signoz/commit/3db8a25eb989c3ac39e54b1dab18c3becda54c4c),
  [signoz#3587](https://github.com/SigNoz/signoz/commit/8bfb0b5088e1a8c9fe8108ef7e2a30f66d92a70d)).
  Also worth a dry-run/preview mode before a rule is saved - run the
  candidate extraction/redaction against a sample of recently-matching
  logs (queried via the same `LogFilter`) and show before/after, rather
  than deploying blind; doesn't need a real OTel collector in the loop,
  just running the extraction logic in-process against sampled
  `LogEvent`s. Prior art: SigNoz's in-memory collector simulator
  ([signoz#3656](https://github.com/SigNoz/signoz/commit/0ad5d671405ae7e98d61cd68ea4d45d654ef0509))
  plus a real bug it later hit worth avoiding from the start - a
  generated rule with no scoping condition silently applies to every
  log line instead of just the ones it's meant for
  ([signoz#3870](https://github.com/SigNoz/signoz/commit/626da7533ef693893a787ae20c4bfc1cb42d3ba4)).
- **JSON-path filtering inside the raw log `Body`.** Flare's bloom-filter
  skip indices (`0001_logs.sql`) and `AttributeFilter` path only cover
  pre-extracted key/value attributes - there's no way to filter on a key
  nested inside unstructured JSON that landed in `Body` itself (e.g. a
  Serilog/`System.Text.Json` payload logged as the message rather than
  promoted to structured attributes), which is common for .NET apps.
  Distinct from the LogQL attribute-map syntax item below (that one is
  about the existing attribute maps, this one is about arbitrary nesting
  inside `Body`). Not started. Would map onto a new `AttributeFilterOperator`
  (or a `LogQl` function) compiled by `LogFilterSqlBuilder`/`LogQlWhereTranslator`
  into ClickHouse `JSONExtractString`/`JSONHas` calls against `Body` -
  no schema migration needed. Prior art: SigNoz's JSON-in-body filtering
  ([signoz#3534](https://github.com/SigNoz/signoz/commit/17ae197bc340348dc1be9b10b8219cdb6dfd48bd),
  [signoz#3544](https://github.com/SigNoz/signoz/commit/ed809474d62911bccff90f241a581acf6b551fb6)).
- **Logs "context" view + permalink to a specific log line.** Clicking a
  log line would show the N lines immediately before/after it in time,
  with a shareable deep link to that exact line. Not started - would
  layer on the existing keyset-pagination cursor (query rows with
  `(Timestamp, EventId)` just above/below the clicked row). Prior art:
  SigNoz's logs-context feature
  ([signoz#3190](https://github.com/SigNoz/signoz/commit/5f89e84eafa7b34c299c0606783e012d4a76c77d)).
- **Per-panel visual thresholds / conditional formatting.** Dashboard
  panels have no way to say "color this red above X" - purely visual
  styling (background/text color on Value panels, a horizontal line on
  time-series charts, cell coloring in tables) driven by ordered
  operator+value+color rules, distinct from alert rules (which notify,
  not style). Not started. Would live entirely in the existing
  render layer each panel type already has - no backend/schema change,
  reuses the same value-formatting path as the metric-unit-formatting
  item above - plus needs a defined precedence rule for when multiple
  thresholds match on the same value. Prior art: SigNoz's threshold
  feature across Value/time-series/table panel types
  ([signoz#3949](https://github.com/SigNoz/signoz/commit/12819113c14e3a29ac773b22e114f8b879e6e870),
  [signoz#3974](https://github.com/SigNoz/signoz/commit/9333fdcd0b3ba922febcd84dca33662fb08d81ff),
  [signoz#4002](https://github.com/SigNoz/signoz/commit/4009ac83febfd1aee122d2075acb0edde5af4bfd)).
- **Cumulative-counter `rate()`/`increase()` metric aggregation.** Flare's
  metrics explorer/`MetricSeriesQueryBuilder` deliberately doesn't
  compute a rate or increase over a cumulative counter today (a named,
  unresolved limitation in that file's own remarks: sums want
  `sum(Value)`, not `max - min`, and a naive running difference breaks
  on counter resets and on ClickHouse not guaranteeing row order across
  partitions). Not started; no design chosen yet. Prior art worth
  copying the shape of, not just the idea: SigNoz's `WINDOW ... PARTITION
  BY <series> ORDER BY ts` + `lagInFrame` pattern, which explicitly
  handles negative deltas (counter resets) and doesn't depend on
  ClickHouse's incidental row order the way `runningDifference` did
  ([signoz#4166](https://github.com/SigNoz/signoz/commit/29b134455747099fa63292ef1fa0b7f05e13e231)).
- **Small dashboard/trace UX polish, worth batching into one PR
  eventually rather than three:** (a) a hover popover on trace waterfall
  spans showing duration/start-time without navigating away
  ([signoz#4241](https://github.com/SigNoz/signoz/commit/752688888677389d22b1dcf22ab519a70afc6522));
  (b) click a `MetricChart` legend entry to isolate that one series,
  click again to restore all - confirmed Flare's existing `hiddenSeries`
  count is only the top-N cardinality cap, not a click-to-isolate toggle
  ([signoz#4226](https://github.com/SigNoz/signoz/commit/55664872bd9d9e2b3fbcce747c55ca7dffa4a717));
  (c) a discard-confirmation prompt when closing a dashboard panel/widget
  editor with an un-staged query change, rather than silently losing it
  ([signoz#4188](https://github.com/SigNoz/signoz/commit/9c1ea0cde9b8d20899c22381dc2f7bd6f0045b63)).
  None started; none need backend/schema changes.
- **Alert notification deep-links should scope into the actual fired
  data, not just the rule.** `AlertMessageFormatter` already builds a
  link on every notification, but it only points at
  `{publicUrl}/alerts?rule={ruleId}` - the rule's own history page, not
  a pre-filtered view of the logs/traces that actually fired it. Not
  started. Since an alert rule's condition is already a `LogFilter`,
  this is cheap: substitute the fired series' actual group-by label
  values into that filter (or append them as new equals-filters if not
  already present), serialize it the same way the dashboard's own
  URL-driven Explorer state does, and append the eval window as the time
  range - link straight into `/logs` or `/traces` scoped to exactly what
  fired, no schema change. Prior art: SigNoz's `ThresholdRule.Eval`
  building this link the same way
  ([signoz#4446](https://github.com/SigNoz/signoz/commit/00b111fbe367e16ef6920586e64b226b7e1cff4a)).
- **Deterministic per-series color on MetricChart.** Confirmed
  index-based today (`color: var(${SERIES_COLOR_VARS[i]})`) - a series'
  color depends on its position in that response's series list, so the
  same series can visibly change color across reloads or panels
  whenever ordering shifts. Not started. Would replace the index lookup
  with a hash of the series' label (e.g. its compact label string) into
  the same fixed palette, so a given series always gets the same color.
  Prior art: SigNoz's per-label color hashing
  ([signoz#4478](https://github.com/SigNoz/signoz/commit/0f44246795aef0050ffee06dbc17e1f139981726)).
- **Soft Y-axis min/max on metric charts.** No axis-bound option today.
  Not started; minor. uPlot (already Flare's charting library, per
  MetricChart) natively supports a *soft* min/max mode that still
  auto-expands past the bound if data exceeds it, unlike a hard fixed
  range - a small per-panel chart option, no data/query change. Prior
  art: [signoz#4287](https://github.com/SigNoz/signoz/commit/5b39dc36d6d788b4fd909c524c8569d0b0d08754).
- **Post-aggregation value filter (`HAVING`) + top-N order-by on metric
  queries.** No way today to ask for e.g. "only series where the
  aggregated value exceeds X" after grouping - only the existing
  cardinality cap (`TopN`/`MaxTopN`, a pre-aggregation ranking
  subquery). Not started. Unlike SigNoz's app-side post-filter over
  in-memory series, this belongs natively in the generated ClickHouse
  SQL as a real `HAVING` clause in `MetricSeriesQueryBuilder`, consistent
  with Flare's every-query-is-ClickHouse-native convention - a different
  implementation shape from the prior art, not a port of it. Prior art:
  [signoz#4381](https://github.com/SigNoz/signoz/commit/be27a92fc9ae1a51c60be1fe85b92d376a1bdbbe).
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
- **Cross-query formula expressions for metrics.** No way today to
  combine two named metric queries with an arbitrary expression (e.g.
  `(A/B)*100` for an error-rate ratio, or `exp`/`log`/`sqrt`) - only
  single-query aggregation. Not started. Would evaluate app-side over
  the already-fetched ClickHouse result rows (join by matching label
  sets per timestamp), the same shape as `HistogramQuantileEstimator`'s
  existing app-side post-processing, not pushed into SQL. Prior art:
  [signoz#4402](https://github.com/SigNoz/signoz/commit/c6581782d03fd8e1c6d0a6916ac51afa5d95e52e).
- **Per-query post-processing functions (metrics and logs).** No
  library today for clamp-min/max, absolute, log2/log10, cumulative-sum,
  smoothing (EWMA/median over N points), or time-shift (re-run a query
  N seconds earlier for week-over-week/day-over-day overlay) - distinct
  from the existing drag-to-zoom comparison mode, which is duration-
  derived, not a reusable shift-and-overlay primitive. Not started.
  Would be a small set of pure, unit-testable transforms in
  `Flare.Api`'s Model/Query layer, applicable to both the metrics and
  logs explorers (SigNoz shipped metrics first, then extended time-shift
  to logs separately). Prior art:
  [signoz#4445](https://github.com/SigNoz/signoz/commit/3b98073ad4f0fe9825ce7e9ac47de1df16c98865),
  [signoz#4569](https://github.com/SigNoz/signoz/commit/1a62a13aeaa205cae3b474f3ad07ab2944385757),
  [signoz#4607](https://github.com/SigNoz/signoz/commit/d0d10daa442e387fe557ae0bb6c14b19d004ef8d).
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
  started. Should include existence checks (`attr has`/`attr not has`,
  i.e. `mapContains`) alongside value comparisons, not just equality -
  one-line addition once the grammar work happens anyway
  ([signoz#3567](https://github.com/SigNoz/signoz/commit/81b10d126a6d51b3381df1f187b819225f2b3473)).
  Design trap to check for, whatever grammar is chosen: OTel semantic-
  convention attribute keys routinely contain literal dots themselves
  (`http.status_code`, `k8s.pod.name`), so a dot-as-path-separator syntax
  (e.g. `attributes.foo`) needs a rule for a dotted key that isn't just
  "split on every dot" - SigNoz hit this collision more than once in
  their own attribute-path handling.
