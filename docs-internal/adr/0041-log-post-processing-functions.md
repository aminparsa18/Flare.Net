# ADR-0041: Per-query post-processing functions - Logs support

Status: Accepted

Date: 2026-09-22

## Context

ADR-0038 shipped the metrics-only v1 slice of the roadmap's "Per-query
post-processing functions (metrics and logs)" item: point-wise
clamp-min/max/absolute/log2/log10 plus a running cumulative-sum, computed
backend-side by `MetricPostProcessor` and applied to `MetricQueryService`'s
already-fetched `MetricSeries`. ADR-0039 added the two window-based
smoothing functions (`EwmaSmoothing`/`MedianSmoothing`). ADR-0040 added a
time-shift overlay, dashboard-only. All three explicitly left Logs support
as a named, still-open follow-up - the roadmap item's own wording notes
SigNoz shipped metrics first, then extended time-shift to logs separately.

This ADR picks up the Logs half for the point-wise/running/smoothing
functions (mirroring ADR-0038/0039's scope, not ADR-0040's time-shift -
see Consequences).

The Logs Explorer has no per-series time-bucketed query the way Metrics
does - its closest equivalent is `VolumeChart.svelte`'s histogram, backed
by `POST /api/logs/aggregate` (`LogAggregateRequest`/`LogQueryService.AggregateAsync`).
Two structural differences from the metrics case needed resolving before
just copying `MetricPostProcessor` over:

1. **No per-series wrapper.** `MetricQueryResponse` returns `MetricSeries[]`,
   each with its own `Points` list - `MetricPostProcessor` runs over one
   series' points at a time, for free. `LogAggregateResponse` has no such
   wrapper: `Buckets` is one flat list, and when `LogAggregateRequest.GroupBy`
   is set, different groups' buckets are interleaved in it, disambiguated
   only by `LogAggregateBucket.GroupKey`.
2. **No nullable value slot.** `MetricSeriesPoint.Value` is `double?` - a
   metric series is pre-aligned to a fixed bucket grid, and a bucket with
   no data becomes a null point, which is exactly what `MetricPostProcessor`'s
   "carry forward through a gap" null semantics (cumulative-sum,
   EWMA/median smoothing) are built around. `LogAggregateBucket.Count` is a
   non-nullable `double` - a bucket only exists in the response because a
   `GROUP BY` matched at least one row there, so there's no "gap" concept
   to carry a running total or smoothed value through in the first place.

## Decision

**A new `LogPostProcessFunctionType`/`LogPostProcessFunction` pair,
field-for-field identical in shape to `MetricPostProcessFunctionType`/
`MetricPostProcessFunction`, applied by a new `LogPostProcessor` to
`LogAggregateResponse.Buckets`, partitioned by `GroupKey` first.**

- `LogAggregateRequest.PostProcessFunctions` is an ordered
  `IReadOnlyList<LogPostProcessFunction>?` chain, appended as the request's
  4th field (`Filter`/`BucketWidthSeconds`/`GroupBy` already existed) - same
  append-only MemoryPack field-versioning convention `MetricQueryRequest.ts`'s
  own `postProcessFunctions` append documents.
- **`LogPostProcessor.Apply`** (new, `Flare.Api/Query/LogPostProcessor.cs`)
  partitions `Buckets` by `GroupKey` (including the ungrouped `null` key as
  its own single partition) into independent series, runs the full function
  chain over each partition separately - so a cumulative-sum or EWMA never
  blends one service's counts into another's running total - then
  reassembles the result in the original list order. `LogQueryService.AggregateAsync`
  is the only caller, same "pure, app-side, post-aggregation" shape
  `MetricQueryService.QueryAsync` already established for the metrics side.
- **No null semantics to design** - unlike `MetricPostProcessor`, every
  function here operates directly on the plain `double` sequence. There's
  no gap to carry a running total/smoothed value through, so
  `CumulativeSum`/`EwmaSmoothing`/`MedianSmoothing` are actually *simpler*
  here than their metrics counterparts (no "was the input null" branch at
  all).
- **Log2/Log10 of a non-positive input returns 0, not null** - the one
  real divergence from `MetricPostProcessor`, forced by `LogAggregateBucket.Count`
  having no nullable slot to return into. Same reasoning
  `MetricPostProcessor` gives for avoiding `NaN`/`-Infinity`: a
  mathematically-undefined input shouldn't propagate a non-finite value
  into a later chain step or into the chart it feeds, and 0 is the same
  "no signal" floor a bucket's count already uses elsewhere.
- **Dashboard**: a new `LogsFunctionsPopover.svelte`, a near-verbatim copy
  of `MetricsFunctionsPopover.svelte` (same chain-editor shape: add/remove
  rows, a type `Select` per row, a value/window-size `Input` depending on
  the row's type) rather than a shared generic component - there's no
  Histogram-style per-type gating concern on the Logs side to abstract
  around, so a copy stays simpler than parameterizing one component for
  two slightly different call sites. Wired into `LogsToolbar.svelte` next
  to "Clear filters".
- **`LogsFilterState.postProcessFunctions`** is a real display preference on
  `VolumeChart`'s bucket counts, same category `MetricsFilterState.postProcessFunctions`
  already established for Metrics - carried through `LogsSavedViewState`/
  `toSavedViewState`/`applySavedViewState` (and therefore through dashboard
  panels too: `DashboardLogsPanelBody.svelte` reuses `LogsExplorerState`
  wholesale, so no panel-body change was needed), but deliberately excluded
  from `hasActiveFilters()`/`resetFilters()` - it's a transform on what the
  chart displays, not a content filter on what the log table searches for.
- **Request-building lives in `VolumeChart.svelte`, not `LogsExplorerState`**
  - unlike `MetricsExplorerState.runQuery`, which is the sole call site
  crossing into the metrics query API, `VolumeChart.svelte`'s own `refresh()`
  is the one place that calls `aggregateLogs()`. `setPostProcessFunctions`
  just writes `LogsFilterState.postProcessFunctions`; `VolumeChart`'s
  existing debounced refetch `$effect` was extended to also read that field
  (so applying a chain re-fetches the chart) and its `refresh()` now passes
  it through to `aggregateLogs()`.
- **Wire format**: `LogPostProcessFunction` has no `DateTimeOffset`/nesting
  problem, so it's a real MemoryPack-TS-generated class (`[GenerateTypeScript]`),
  reused directly from the hand-written `LogAggregateRequest.ts` the same
  way `MetricQueryRequest.ts` reuses the generated `MetricPostProcessFunction`.

## Consequences

- Time-shift for Logs (re-running the aggregate query at a fixed offset,
  overlaying the result on the volume chart) remains the one still-open
  half of the roadmap item - ADR-0040 shipped it for Metrics only, and
  nothing here assumes or blocks a Logs-side equivalent. Whoever picks it
  up next should look at ADR-0040's `shiftRange`/`overlayRange` plus
  `VolumeChart.svelte`'s own single-series bar-chart rendering (no overlay
  line concept exists there yet, unlike `MetricChart`) as the closest
  precedent and the biggest gap to close.
- A post-processed bucket count can go negative (e.g. `ClampMax` with a
  negative threshold, or a chain that overshoots) or non-integral (e.g.
  `EwmaSmoothing`) even though `VolumeChart`'s bars were designed around
  non-negative integer counts. Not specially handled: `barHeight`'s
  existing `Math.max(MIN_BAR_HEIGHT, ...)` floor means a negative value
  renders as a minimal-height bar rather than crashing or drawing a
  negative-height `<rect>`, and the "N events" total label above the chart
  becomes "sum of transformed values" rather than a literal event count
  when a chain is active - the same "the chart just shows whatever came
  back" philosophy ADR-0038 already established (`MetricChart.svelte`
  needed no changes for the same reason).
- 888/888 `Flare.Api.Tests` (21 new `LogPostProcessorTests`, including a
  grouped-partitioning case with no metrics-side analog), `svelte-check`/
  `vite build` clean. Live e2e not done, consistent with several other
  recent dashboard-only/query-layer changes in this codebase that skipped
  it when not explicitly requested.

## Related documentation

- `src/Flare.Api/Model/LogAggregateRequest.cs` - `LogPostProcessFunctionType`/
  `LogPostProcessFunction`/`LogAggregateRequest.PostProcessFunctions`.
- `src/Flare.Api/Query/LogPostProcessor.cs` - the pure, per-`GroupKey`
  transform chain this ADR describes, and its own remarks on why its null
  semantics diverge from `MetricPostProcessor`'s.
- `src/dashboard/src/lib/components/logs/LogsFunctionsPopover.svelte` - the
  chain editor.
- `src/dashboard/src/lib/logs/state.svelte.ts` -
  `LogsFilterState.postProcessFunctions`/`setPostProcessFunctions`.
- `src/dashboard/src/lib/components/logs/VolumeChart.svelte` - the one
  fetch call site this chain flows through.
- `docs-internal/adr/0038-metric-post-processing-functions.md` - the
  metrics v1 scope decision and the point-wise/running functions this ADR
  ports to Logs.
- `docs-internal/adr/0039-metric-smoothing-post-processing-functions.md` -
  the smoothing functions this ADR also ports.
- `docs-internal/adr/0040-metric-time-shift-overlay.md` - the still-open
  time-shift follow-up this ADR does not extend to Logs.
- Prior art: SigNoz's post-processing function pipeline, named in the
  original roadmap item
  ([signoz#4445](https://github.com/SigNoz/signoz/commit/3b98073ad4f0fe9825ce7e9ac47de1df16c98865),
  [signoz#4569](https://github.com/SigNoz/signoz/commit/1a62a13aeaa205cae3b474f3ad07ab2944385757),
  [signoz#4607](https://github.com/SigNoz/signoz/commit/d0d10daa442e387fe557ae0bb6c14b19d004ef8d)).
