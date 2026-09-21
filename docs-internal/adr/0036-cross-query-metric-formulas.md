# ADR-0036: Cross-query metric formulas as app-side, Explorer-only Formula mode

Status: Accepted

Date: 2026-09-21

## Context

`docs-internal/planning/roadmap.md` flagged that there was no way to combine
two named metric queries with an arbitrary expression (e.g. `(A/B)*100` for
an error-rate ratio) - only single-query aggregation. The item's own note
already sketched the shape: evaluate app-side over already-fetched
`MetricQueryResponse` rows, joined by matching label sets per timestamp, the
same shape `HistogramQuantileEstimator` already uses for its own app-side
post-processing on the backend - not a new SQL query shape.

`MetricsExplorerState` (`src/dashboard/src/lib/metrics/state.svelte.ts`) was
built entirely around one selected `(metricName, serviceName)` pair -
`selected`, `series`, `resultType`, comparison mode, Sum rate/count modes,
Histogram percentile modes. None of that has an obvious meaning for a
formula result: there's no single metric name/unit/type, and the "series"
concept becomes N *joined* (serviceName, attributes) pairs instead of N
independently-fetched ones. `MetricChart.svelte` is a large (1000+ line),
delicate file built entirely around that same single-selection shape.

Two real scope questions needed deciding up front, both resolved with the
user before implementation:

1. **Where does this ship first** - Metrics Explorer only, or Explorer +
   dashboard panels together? Dashboard Metrics panels
   (`DashboardMetricsPanelBody.svelte`) reuse `MetricsExplorerState` +
   `MetricChart.svelte` wholesale (see that file's own header comment) -
   giving them Formula mode too would mean either teaching `MetricChart`
   a second, structurally different rendering mode, or building a second
   panel-body component, on top of the Explorer-side work.
2. **How do Histogram metrics participate** - a formula operand needs one
   scalar `Value` per bucket (what `MetricSeriesPoint.Value` already is for
   Gauge/Sum), but Histogram's per-bucket shape is percentiles/sum/count,
   not a single value. Excluding Histograms sidesteps picking a default
   projection (p95? sum? mean?) that would be a real, separate design
   decision.

## Decision

**Scope: Metrics Explorer only, Gauge/Sum metrics only, v1.**

- A new `MetricsExplorerState.mode: 'single' | 'formula'` toggle
  (`MetricsToolbar`'s new mode `Select`, next to the time-range preset
  picker). Switching modes never clears the other mode's state - flipping
  back to `'single'` shows whatever was last loaded there, not a blank
  picker.
- Formula mode adds a fully separate set of fields
  (`formulaQueries`/`formulaExpression`/`formulaSeries`/...) rather than
  reusing `selected`/`series`/`resultType` - see this file's Context section
  for why those single-mode fields don't generalize. Two new, small,
  dedicated components - `FormulaBuilder.svelte` (the query-row editor,
  `MetricPicker.svelte`'s formula-mode equivalent) and `FormulaChart.svelte`
  (a much smaller hand-rolled-SVG chart, the same technique
  `MetricChart.svelte`/`VolumeChart.svelte` already use, minus every mode
  `MetricChart` has that a formula result has no equivalent of: no Sum
  rate/count picker, no Histogram percentile picker, no comparison-period
  lines, no drag-to-zoom, no 5-color-slot-plus-"+N more" cap) - not new
  modes bolted onto `MetricPicker`/`MetricChart` themselves. Keeps the
  large, delicate `MetricChart.svelte` file completely untouched by this
  change.
- Each named query row (`A`, `B`, ... up to `MAX_FORMULA_QUERIES` = 6) is
  an independent metric selection + optional `groupByAttributeKey`, fetched
  via the *existing* `POST /api/metrics/query` endpoint - one call per
  referenced letter, run in parallel via `Promise.all`, exactly like
  `runQuery`'s existing current/previous-period parallel fetch for compare
  mode. **No backend changes at all** - no new endpoint, no new
  `MetricQueryRequest`/`MetricSeriesPoint` field. This is the
  roadmap item's own "app-side, not pushed into SQL" framing taken
  literally: the join/evaluate step is pure TypeScript
  (`src/dashboard/src/lib/metrics/formula.ts`), with no ClickHouse or API
  dependency, mirroring how `HistogramQuantileEstimator.cs` is pure C# with
  no ClickHouse dependency on the backend side of the same "post-process
  already-fetched rows" pattern.
- **Join key**: `ServiceName` + the series' `attributes` map (sorted,
  stringified) - the same (service, attribute-set) identity
  `MetricSeriesQueryBuilder`'s own remarks already use to define a
  `MetricSeries`' identity, reused here as the cross-query join key rather
  than inventing a new one. An **inner join** throughout (on join key, then
  on bucket timestamp within a matched key) - never a left join defaulting
  a missing operand to 0, which would fabricate data points (e.g. `A/B`
  spiking to 0 the instant `B` has no data for a bucket, instead of
  correctly having no point there at all). A join producing zero series
  (e.g. two queries grouped by different attribute keys, so their
  attribute maps never structurally match) surfaces as a non-fatal
  `formulaWarning` hint, not an error - confirmed live during verification
  (see below): `dotnet.process.cpu.time`'s `cpu.mode` attribute doesn't
  match `dotnet.gc.heap.total_allocated`'s empty attribute set, so that
  real pairing correctly produces "no matching data points" with a hint to
  align Group by, while `dotnet.gc.heap.total_allocated /
  dotnet.process.memory.working_set` (both attribute-less) joins and
  charts correctly.
- **Expression grammar**: `+ - * /`, unary minus, parens, and exactly the
  three functions the roadmap item named - `exp`/`log`/`sqrt` - via a small
  hand-rolled recursive-descent parser (`parseFormula`/`evaluateFormula` in
  `formula.ts`), not a general expression-evaluation library or `eval()`
  (a user-authored string should never reach `eval`/`Function`, regardless
  of trust level, per this codebase's own security posture elsewhere). No
  `^`/power operator - not asked for, and it would need its own precedence
  tier.
- **Histogram metrics excluded from the Formula-mode metric picker
  entirely** (`FormulaBuilder.svelte` filters `explorer.names` to
  non-Histogram types) - a named v1 scope cut, not an oversight. Picking a
  histogram's projection (p95? mean? sum?) as its formula-operand value is
  a real, separate design question load-bearing enough to deserve its own
  decision later, not a silent default now.
- **No TopN/Having override per row, no compare-period fetch** - v1 scope
  cuts, same "named, deliberately-unresolved limitation" convention this
  codebase already uses elsewhere (e.g. `HistogramQuantileEstimator`'s own
  remarks). Every row gets the server's own default series cap
  (`MetricSeriesQueryBuilder.DefaultTopN` = 20), which is also why
  `evaluateFormula`'s own defensive `MAX_OUTPUT_SERIES` cap (50) is never
  expected to bind - an inner join can't exceed its smallest input.
- **`PinToDashboardButton` hidden in Formula mode** rather than pinning
  something that can't render: `DashboardMetricsPanelBody.svelte` reuses
  `MetricChart.svelte`, which has no Formula-mode rendering, so a pinned
  Formula-mode saved-view state would silently produce a panel showing
  nothing. `MetricsSavedViewState` does carry `mode`/`formulaExpression`/
  `formulaQueries` (so Formula mode round-trips through the page's own
  `ViewsMenu` saved views correctly), it's specifically the
  dashboard-*panel* path that's blocked, not saved views generally.

## Consequences

- Dashboard Metrics panels still don't support Formula mode - left as a
  named, still-open roadmap follow-up (`docs-internal/planning/roadmap.md`),
  not silently dropped. Whoever picks it up next will need to decide
  whether `DashboardMetricsPanelBody` grows a second rendering path or a
  new panel type is introduced.
- Histogram-metric formula operands remain unsupported - same status,
  follow-up work, not a bug.
- No new backend surface to maintain: `MetricQueryRequest`/
  `MetricSeriesPoint`/`MetricSeriesQueryBuilder` are completely unchanged
  by this ADR. The entire feature is additive dashboard-side code
  (`$lib/metrics/formula.ts`, `state.svelte.ts` additions,
  `FormulaBuilder.svelte`, `FormulaChart.svelte`, `MetricsToolbar.svelte`'s
  new mode picker).
- Live-verified end-to-end via `playwright-cli` against a real
  docker-compose stack seeded by `ExampleApp.LogGenerator`'s real
  OTel runtime metrics (not synthetic fixtures): mode toggle, per-row
  metric picker correctly excluding Histogram, add/remove query rows,
  expression parsing (valid formulas, `sqrt()`/functions, and a real
  syntax error showing an inline message without clearing the last-good
  chart), the join actually producing a rendered multi-point line chart
  for a real ratio (`dotnet.gc.heap.total_allocated /
  dotnet.process.memory.working_set`), the empty-join warning path for a
  genuinely mismatched pairing, mode-switch state preservation in both
  directions, and confirming only the pre-existing
  `POST /api/metrics/query`/`/api/metrics/names`/`/api/metrics/attribute-keys`
  endpoints were called (no new network surface). `svelte-check` and
  `vite build` both clean.

## Related documentation

- `src/dashboard/src/lib/metrics/formula.ts` - `parseFormula`/
  `collectRefs`/`evaluateFormula`, the pure parser/join/evaluator this ADR
  describes.
- `src/dashboard/src/lib/metrics/state.svelte.ts` - `MetricsExplorerState`'s
  `mode`/`formulaQueries`/`formulaExpression`/`runFormulaQuery` and
  `MetricsExplorerMode`'s own remarks on why formula mode is a fully
  separate set of fields.
- `src/Flare.Api/Query/HistogramQuantileEstimator.cs` - the backend-side
  precedent for "pure, app-side post-processing of already-fetched rows,"
  the same shape this ADR applies dashboard-side.
- `src/Flare.Api/Query/MetricSeriesQueryBuilder.cs` - the `remarks` doc
  comment defining the (`ServiceName`, attributes) series identity this
  ADR's join key reuses.
- Prior art: SigNoz's formula-expression queries
  ([signoz#4402](https://github.com/SigNoz/signoz/commit/c6581782d03fd8e1c6d0a6916ac51afa5d95e52e)),
  named in the original roadmap item.
