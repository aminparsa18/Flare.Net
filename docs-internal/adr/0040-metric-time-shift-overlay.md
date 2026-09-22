# ADR-0040: Metric time-shift overlay

Status: Accepted

Date: 2026-09-22

## Context

ADR-0038 shipped point-wise/running post-processing functions and ADR-0039
added smoothing, both metrics-only slices of the roadmap's "Per-query
post-processing functions (metrics and logs)" item. Both left time-shift as
a named, deliberately-unresolved follow-up: "re-run a query N seconds
earlier for week-over-week/day-over-day overlay - structurally closer to
the existing compare-mode previous-period fetch than to a point-wise
transform." This ADR picks that up.

`MetricsToolbar`'s existing "Compare with previous period" switch
(`MetricsFilterState.compareEnabled`) already does something close: a
second, parallel `queryMetric` call one period back
(`previousPeriod(range)`), overlaid on the chart shifted forward to land on
the same x-axis. But its shift is *duration-derived* - always exactly the
displayed range's own width (a 1h view compares to the 1h before it, a 6h
view to the 6h before that). It can't express "this time last week"
regardless of whether the displayed range happens to be 1 hour or 6 hours
wide - the roadmap item's own wording flags this as a real gap, distinct
from the existing drag-to-zoom comparison mode's own reasoning.

Two scope questions were decided with the user before implementation, same
as ADR-0038's own Context:

1. **Interaction with the existing Compare switch** - allow both overlays
   on at once (up to 3 lines: current, duration-previous, time-shifted), or
   make them mutually exclusive? Decided: **mutually exclusive**, one
   overlay line at a time.
2. **Shift-offset input shape** - a closed set of presets only, or presets
   plus a custom value? Decided: **presets (1 hour / 24 hours / 7 days)
   plus a custom value + unit (hours/days) escape hatch**, same "closed set
   + custom" shape `MetricsFunctionsPopover`'s window-size input already
   established for a numeric parameter in this same control family.

## Decision

**A new `MetricsFilterState.timeShiftSeconds: number | null` (dashboard-only
- no new backend field, no wire-format change), mutually exclusive with
`compareEnabled`, driving a generalized version of the existing
current+overlay parallel-fetch/render pipeline.**

- **No `Flare.Api` changes at all.** Unlike ADR-0038/0039 (real backend
  transforms), time-shift is purely a *different range* sent to the same
  `POST /api/metrics/query` endpoint the overlay fetch already calls for
  compare mode - "a second query dispatch and a result-alignment step,"
  exactly as ADR-0038's own Consequences predicted, not a new
  `MetricPostProcessFunctionType`. `MetricQueryRequest` is untouched.
- **`shiftRange(range, seconds)`** (`$lib/logs/time-range.ts`), the
  fixed-offset counterpart to the existing `previousPeriod(range)`
  (duration-derived) - shifts both ends of a `ResolvedTimeRange` back by
  the same arbitrary `seconds`, independent of the range's own width.
- **`MetricsExplorerState.runQuery`** generalizes its existing
  current/previous `Promise.all` into current/*overlay*: `overlayRange =
  compareEnabled ? previousPeriod(range) : timeShiftSeconds != null ?
  shiftRange(range, timeShiftSeconds) : null`. The overlay fetch itself
  (best-effort, `.catch(() => null)`, never blocks `series`) and the field
  it lands in (`previousSeries`) are unchanged and reused as-is for either
  mode - the two are mutually exclusive, so there's never ambiguity about
  which mode produced it, only `resultCompareEnabled` and the new
  `resultTimeShiftSeconds` (same "as of the query, not the live filter, no
  blink" pairing) say which.
- **Mutual exclusion enforced in the two setters**, not just left as a UI
  convention: `setCompareEnabled(true)` clears `timeShiftSeconds`;
  `setTimeShiftSeconds(non-null)` clears `compareEnabled`. Applies to
  saved-view restore too (both fields round-trip through
  `MetricsSavedViewState`, defaulting to off for pre-ADR-0040 saved views).
- **`MetricChart.svelte`** generalizes `compareActive`/`compareUnavailable`
  into a parallel `timeShiftActive`/`timeShiftUnavailable` pair (identical
  Histogram Mean/Max-only availability, identical "keyed off the result
  field, not the live filter" reasoning) plus a new `overlayActive =
  compareActive || timeShiftActive` that every generic "is the chart
  showing the 2-line overlay shape" decision now branches on
  (`hiddenSeriesCount`, `lines`, `chartKey`, the "(summed)" series-count
  label) instead of `compareActive` alone. `buildComparisonLines` picks its
  shift/label via a new `overlayShiftMsAndLabel()`: compare keeps its
  duration-derived shift and "Previous" label; time-shift uses the fixed
  offset and a `"{duration} ago"` label (`formatBucketWidthSeconds` - the
  same compact `1h`/`24h`/`7d` formatter `MetricChart`'s own interval label
  already uses, reused here instead of writing a second duration
  formatter). The percent-change summary/tooltip (`comparePercent`/
  `compareChangeText`/`compareRangeDetail`) generalize the same way, so
  "+12% vs 7d ago" reads next to the chart exactly where "+34% vs previous
  24 hours" already did for compare mode.
- **Dashboard control**: new `MetricsTimeShiftPopover.svelte`, same "small
  icon-triggered popover with a mini form" shape as
  `MetricsHavingPopover.svelte` - a `Select` (Off / 1h / 24h / 7d /
  Custom…) plus, only for Custom, a number input + Hours/Days unit
  `Select`. Placed in `MetricsToolbar.svelte` right after the Compare
  switch, single-mode only (Formula mode has no overlay fetch at all yet,
  same v1 scope cut Compare's own remarks already document).
- **No dashboard-panel changes needed** - `DashboardMetricsPanelBody.svelte`
  reuses `MetricsExplorerState`/`MetricChart` wholesale, same free ride
  ADR-0038's `postProcessFunctions` and ADR-0039's smoothing already got.

## Consequences

- Logs support (the roadmap item's other still-open half) remains
  untouched - nothing here assumes or blocks a future `LogPostProcessor`
  or a Logs-explorer time-shift, per the item's own SigNoz-precedent note
  that metrics shipped first there too.
- Compare and time-shift can never both drive the overlay at once by
  construction (each setter clears the other's field), so `MetricChart`
  never has to reconcile two different shift sources or render a 3-line
  chart - a real scope cut, not an oversight, per the user's own choice
  above.
- `svelte-check`/`vite build` clean. No backend changes, so no new
  `Flare.Api.Tests`. Live e2e not done (v1 scope, consistent with several
  other recent dashboard-only changes in this codebase that skipped it
  when not requested).

## Related documentation

- `src/dashboard/src/lib/logs/time-range.ts` - `shiftRange`, alongside the
  existing `previousPeriod` it mirrors.
- `src/dashboard/src/lib/metrics/state.svelte.ts` -
  `MetricsFilterState.timeShiftSeconds`, `resultTimeShiftSeconds`,
  `setTimeShiftSeconds`/`setCompareEnabled`'s mutual-exclusion remarks, and
  `runQuery`'s generalized `overlayRange`.
- `src/dashboard/src/lib/components/metrics/MetricChart.svelte` -
  `timeShiftActive`/`timeShiftUnavailable`/`overlayActive`,
  `overlayShiftMsAndLabel`.
- `src/dashboard/src/lib/components/metrics/MetricsTimeShiftPopover.svelte` -
  the toolbar control.
- `docs-internal/adr/0038-metric-post-processing-functions.md` - the v1
  scope decision that named this as a follow-up and pointed at `runQuery`'s
  existing parallel fetch as the closest precedent.
- Prior art: SigNoz's post-processing/time-shift pipeline, named in the
  original roadmap item
  ([signoz#4445](https://github.com/SigNoz/signoz/commit/3b98073ad4f0fe9825ce7e9ac47de1df16c98865),
  [signoz#4569](https://github.com/SigNoz/signoz/commit/1a62a13aeaa205cae3b474f3ad07ab2944385757),
  [signoz#4607](https://github.com/SigNoz/signoz/commit/d0d10daa442e387fe557ae0bb6c14b19d004ef8d)).
