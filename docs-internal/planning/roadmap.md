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
- **Dependency-graph edges pre-aggregated at flush time, not queried
  live.** The Services-tab Table view's RED metrics (ADR-0030) and, as of
  ADR-0031, the Map view's nodes (`service_dependency_nodes` +
  `service_dependency_nodes_mv`) and the per-node call breakdown
  (`service_call_breakdown_external`/`service_call_breakdown_database`) are
  all pre-aggregated now. `ServiceDependencyQueryBuilder`'s **edges** query
  - a self-join keyed by the `peer.service`-overridden "effective service,"
  producing `(source, target)` pairs - still self-joins raw `spans` on
  every Map-view load, and stays that way for now. The dual-triggered
  materialized-view self-join ADR-0031 sketched (one MV per join direction,
  joining the new batch against the *full* persisted `spans` table for the
  other side) was live-spiked on 2026-09-21 and found **not viable, not just
  risky**: see `docs-internal/investigations/service-dependency-edges-mv-selfjoin-spike.md`
  for the full evidence, but the short version is that a ClickHouse
  materialized view's self-join resolves *both* sides of the join - the
  trigger's own `FROM` and any `JOIN` back to the same table - to the single
  newly-inserted block, never the already-committed table. That means the
  *common* cross-batch case (parent/child spans from different
  processes/exporters landing in different `SpanFlushWorker` flush batches -
  confirmed by reading `SpanFlushWorker.FlushAsync`, and the case ADR-0031's
  Context calls "the common case, not an edge case") **never produces an
  edge at all**, while the rarer same-batch case produces a **doubled**
  count instead of a correct one. Not a tunable edge case - a different
  design is needed, not a fix to this one. The named prior-art alternative,
  not yet explored: SigNoz's `USE_SPAN_METRIC` feature flag sources the same
  page from collector-generated span metrics instead of live trace
  aggregation
  ([signoz#3134](https://github.com/SigNoz/signoz/commit/433f930956db03c01bcbd72ea61e43bce1eddc57),
  [signoz#3188](https://github.com/SigNoz/signoz/commit/bc4a4edc7f8ac5d2b1bbcca642927df1cc37c9af),
  [signoz#3196](https://github.com/SigNoz/signoz/commit/562621a1171a5cbdb7d11a4e24f0c8fe2199b1da)) -
  i.e. computing edges from OTel Collector-side span-to-span-metrics
  connectors before spans ever reach ClickHouse, sidestepping the
  self-join-visibility problem entirely rather than working around it in SQL.
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
- **Metric-threshold alert evaluation still has the counter-reset blind
  spot `MetricSeriesQueryBuilder`'s chart query used to have.**
  `MetricAlertConditionQueryBuilder` (ADR-0020) still computes Sum's
  `Value` as `max(Value) - min(Value)` over the whole evaluation window,
  same as `MetricSeriesQueryBuilder` did before ADR-0035 replaced that
  with a windowed, reset-aware `increase()` there. A counter reset
  (process restart) mid-window can still read as a dip - or a wrongly
  negative threshold comparison - for an alert rule, not just a chart.
  Not started; surfaced as a named, separate gap while shipping
  ADR-0035, not silently ported alongside it. Same window-function
  approach that ADR-0035 already validated against real ClickHouse
  should carry over here, adapted to a single whole-window scalar
  instead of a bucketed series.
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
- **LogQL `json()` accessor for JSON-path filtering into `Body`.** The
  structured filter path now supports this (`LogFilter.BodyJsonFilters`,
  compiled by `LogFilterSqlBuilder`'s `BodyJsonClause` into ClickHouse
  `JSONHas`/`JSONExtractString` calls against `Body`, mirrored for
  live-tail by `LogFilterMatcher`, and exposed in the dashboard as the
  Logs Explorer's "JSON path filters" row) - but it's unreachable from the
  LogQL query bar (`LogQlLexer`/`LogQlParser`/`LogQlAst`/
  `LogQlWhereTranslator`), which only knows the fixed column list. Not
  started. Would need a new grammar form (e.g. `json(Body, "user.id") =
  "42"`) rather than a bare column reference, a new AST node, and
  translator support emitting the same variadic-key `JSONHas`/
  `JSONExtractString` calls `BodyJsonClause` already uses (confirmed live
  that ClickHouse's JSON functions take one key/index per argument, not a
  single JSONPath string - see `BodyJsonFilter`'s own remarks). Same
  dotted-key collision trap as the attribute-map item above doesn't apply
  here (JSON object keys aren't OTel semantic-convention attribute keys),
  but array-index path segments are still unsupported by the structured
  version and would need their own grammar/translator handling if LogQL
  wants to support them.
