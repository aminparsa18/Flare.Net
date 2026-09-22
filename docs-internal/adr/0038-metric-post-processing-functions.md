# ADR-0038: Per-query metric post-processing functions, backend-side, metrics-only v1

Status: Accepted

Date: 2026-09-22

## Context

`docs-internal/planning/roadmap.md` flagged "Per-query post-processing
functions (metrics and logs)" - no library today for clamp-min/max,
absolute, log2/log10, cumulative-sum, smoothing (EWMA/median over N
points), or time-shift (re-run a query N seconds earlier for
week-over-week/day-over-day overlay), applicable to both the Metrics and
Logs explorers. The item's own note already flagged this is distinct from
the existing drag-to-zoom comparison mode (ADR-0031's/MetricChart's
duration-derived previous-period lines), which isn't a reusable
shift-and-overlay primitive, and that SigNoz itself shipped metrics first,
then extended time-shift to logs separately.

Two scope questions needed deciding up front, both resolved with the user
before implementation:

1. **How much of the item ships in v1** - the full set (7 functions across
   both explorers) is really three separable features: point-wise/running
   transforms (clamp/absolute/log/cumulative-sum), smoothing (a
   window-based transform needing a lookback parameter), and time-shift (a
   second query dispatch plus a result-alignment step against the primary
   series - structurally closer to the existing compare-mode fetch than to
   a point-wise transform). Bundling all three into one change, across two
   explorers, is a large surface for one PR.
2. **Backend or dashboard-side** - ADR-0036's cross-query Formula mode
   deliberately chose pure app-side TypeScript with zero backend changes.
   This item's own roadmap wording, unlike ADR-0036's, explicitly asks for
   "pure, unit-testable transforms in `Flare.Api`'s Model/Query layer" -
   the opposite default.

## Decision

**Scope: Metrics Explorer only, point-wise clamp-min/max/absolute/log2/log10
plus cumulative-sum, computed backend-side. Smoothing, time-shift, and Logs
support are named follow-ups, not silently dropped.**

- New `MetricPostProcessFunctionType` enum (`ClampMin`/`ClampMax`/
  `Absolute`/`Log2`/`Log10`/`CumulativeSum`) and `MetricPostProcessFunction`
  record (`Type` + optional `Value`, the threshold `ClampMin`/`ClampMax`
  need) in `MetricModels.cs`. `MetricQueryRequest.PostProcessFunctions` is
  an ordered `IReadOnlyList<MetricPostProcessFunction>?` - a *chain*, not a
  single function, each step's output feeding the next (e.g. `Absolute`
  then `ClampMax` to fold negative spikes in before capping the top), the
  same "ordered list of steps" shape SigNoz's own post-processing pipeline
  uses (see Prior art below).
- **Pure, app-side, post-aggregation** - `MetricPostProcessor.Apply` (new,
  `Flare.Api/Query/MetricPostProcessor.cs`) runs over the
  already-fetched `MetricSeriesPoint` list per series, the same "no
  ClickHouse dependency, `MetricQueryService` is the only caller" shape
  `HistogramQuantileEstimator` already established for the metrics query
  path. Not pushed into SQL - clamp/log/cumulative-sum over a small
  per-series point list has no meaningful performance reason to run inside
  ClickHouse instead, and keeping it app-side keeps `MetricSeriesQueryBuilder`
  (already a large, ADR-0035-constrained file) completely untouched.
- **Histogram excluded entirely** - `MetricSeriesPoint` has no single
  scalar `Value` for a Histogram bucket (percentiles/sum/count instead),
  the same v1 exclusion ADR-0036 made for Formula-mode operands.
  `MetricQueryService.QueryAsync` only calls `MetricPostProcessor.Apply`
  when the request's type isn't `Histogram`; `MetricsToolbar.svelte` hides
  the control for the same reason, and `MetricsExplorerState.runQuery`
  enforces the exclusion a second time at its own call site rather than
  trusting the toolbar alone.
- **Null semantics, decided per function**: `ClampMin`/`ClampMax`/`Absolute`
  are null-in/null-out (a bucket with no data stays empty). `Log2`/`Log10`
  of a non-positive input return null rather than `NaN`/`-Infinity` -
  mathematically undefined, and a non-finite value would otherwise
  propagate into a later step in the same chain or into the chart it
  feeds. `CumulativeSum` is the one exception: a null bucket is treated as
  "nothing new happened," so the running total carries forward unchanged
  instead of the output itself going null - the result is a genuine
  running total end to end, never punctured by gaps.
- **Dashboard**: a new `MetricsFunctionsPopover.svelte` (chain
  editor - add/remove rows, a type `Select` per row, a value `Input` only
  for `ClampMin`/`ClampMax`) in `MetricsToolbar.svelte`, single mode only,
  same "small icon-triggered popover with a mini form" shape as
  `MetricsHavingPopover.svelte`. `MetricsFilterState.postProcessFunctions`
  is a real display preference (same category as `compareEnabled`/
  `groupByAttributeKey`/`topN`) - carried through `toSavedViewState`/
  `applySavedViewState`, and therefore through dashboard panels too
  (`DashboardMetricsPanelBody.svelte` reuses `MetricsExplorerState`
  wholesale, so no panel-body change was needed - same free ride
  `MetricsHavingPopover`'s own HAVING filter already gets). `MetricChart.svelte`
  itself needed no change: it already just renders whatever `series` it's
  given, and the transform has already been applied by the time the
  response arrives.
- **Wire format**: `MetricPostProcessFunction` has no `DateTimeOffset`/
  nesting problem, so it's a real MemoryPack-TS-generated class
  (`[GenerateTypeScript]`), reused directly from the hand-written
  `MetricQueryRequest.ts` the same way that file already reuses the
  generated `MetricAttributeFilter` via `MetricFilter.ts`.
  `postProcessFunctions` was appended as `MetricQueryRequest`'s 9th field,
  same versioning convention (append-only, older-count fallback) `havingOperator`/
  `havingValue` already established when *they* were appended.

## Consequences

- Smoothing (EWMA/median-over-N) is a still-open roadmap follow-up - it
  needs a window/lookback parameter this chain shape doesn't have yet, a
  real (if small) design question of its own, not an oversight.
- Time-shift (re-run a query N seconds earlier, overlay the result) is a
  still-open roadmap follow-up, and structurally different from this
  pipeline: it needs a second query dispatch and a result-alignment step
  against the primary series, closer in shape to the existing
  compare-mode previous-period fetch than to a point-wise/running
  transform. Whoever picks it up next should look at `runQuery`'s existing
  parallel current/previous fetch for the closest precedent in this
  codebase, not at `MetricPostProcessor`.
- Logs support is a still-open roadmap follow-up, per the item's own note
  that SigNoz shipped metrics first, then extended time-shift to logs
  separately - nothing here assumes or blocks a future `LogPostProcessor`.
- 857/857 `Flare.Api.Tests` (14 new `MetricPostProcessorTests`),
  `svelte-check`/`vite build` clean, and full live e2e via `playwright-cli`
  against a real `docker compose` stack seeded by
  `ExampleApp.LogGenerator`'s real OTel runtime metrics: selecting a Sum
  metric (`dotnet.process.memory.working_set`) shows the "Functions"
  control; selecting a Histogram (`dns.lookup.duration`) hides it entirely;
  applying `Absolute` alone round-trips a successful `POST /api/metrics/query`
  and updates the active-count label; chaining `Absolute` -&gt; `Clamp max(1)`
  visibly re-scales the chart's y-axis from ~0-200 KB/s down to ~0.01-0.02
  B/s, concrete proof the clamp actually reached the plotted values, not
  just the UI label; "Clear" reverts the chart to the original ~0-200 KB/s
  scale. Live e2e also caught and fixed a real bug: `MetricsFunctionsPopover`'s
  `apply()` called `.trim()` directly on a clamp row's numeric input value,
  which throws once Svelte's native `bind:value` on `<input type="number">`
  coerces it from the initial `''` string to a real `number` - the exact
  gotcha `MetricsHavingPopover.svelte`'s own `valueDraft` remarks already
  named, missed here on the first pass. Fixed by widening the draft row's
  type to `string | number` and routing through `String(...)` first, same
  as the Having popover already does.

## Related documentation

- `src/Flare.Api/Model/MetricModels.cs` - `MetricPostProcessFunctionType`/
  `MetricPostProcessFunction`/`MetricQueryRequest.PostProcessFunctions`.
- `src/Flare.Api/Query/MetricPostProcessor.cs` - the pure transform chain
  this ADR describes, and its own remarks on null semantics per function.
- `src/Flare.Api/Query/HistogramQuantileEstimator.cs` - the backend-side
  precedent for "pure, app-side post-processing of already-fetched rows."
- `src/dashboard/src/lib/components/metrics/MetricsFunctionsPopover.svelte` -
  the chain editor.
- `src/dashboard/src/lib/metrics/state.svelte.ts` -
  `MetricsFilterState.postProcessFunctions`/`setPostProcessFunctions`.
- `docs-internal/adr/0036-cross-query-metric-formulas.md` - the Histogram
  exclusion precedent this ADR reuses, and the contrasting "pure app-side
  TypeScript, zero backend changes" choice this ADR deliberately diverges
  from (per the roadmap item's own backend-layer wording).
- Prior art: SigNoz's post-processing function pipeline, named in the
  original roadmap item
  ([signoz#4445](https://github.com/SigNoz/signoz/commit/3b98073ad4f0fe9825ce7e9ac47de1df16c98865),
  [signoz#4569](https://github.com/SigNoz/signoz/commit/1a62a13aeaa205cae3b474f3ad07ab2944385757),
  [signoz#4607](https://github.com/SigNoz/signoz/commit/d0d10daa442e387fe557ae0bb6c14b19d004ef8d)).
