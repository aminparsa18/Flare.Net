# ADR-0042: Log time-shift overlay

Status: Accepted

Date: 2026-09-23

## Context

ADR-0040 shipped a fixed-offset ("this time last week") time-shift overlay
for the Metrics Explorer, mutually exclusive with `MetricsToolbar`'s
duration-derived "Compare with previous period" switch. ADR-0041 then
ported the rest of ADR-0038/0039's post-processing chain
(clamp-min/max/absolute/log2/log10/cumulative-sum plus EWMA/median
smoothing) to the Logs Explorer's volume chart, explicitly leaving
time-shift as the one still-open half of the roadmap's "Per-query
post-processing functions (metrics and logs)" item - its own Consequences
pointed at ADR-0040's `shiftRange`/`overlayRange` as the closest precedent
and at `VolumeChart.svelte`'s lack of any overlay-line rendering concept
(unlike `MetricChart`) as the biggest gap to close. This ADR closes it.

Two structural differences from the Metrics case needed resolving:

1. **No existing overlay control to be mutually exclusive with.** Logs has
   no "Compare with previous period" switch the way Metrics does - the
   time-shift popover is the first and only overlay control on the volume
   chart, so ADR-0040's mutual-exclusion setter logic has nothing to apply
   to here.
2. **No real x-axis to align an overlay onto.** `MetricChart` plots points
   at real epoch-ms positions and shifts an overlay point's `time` forward
   by the offset so it lands exactly on its current-period counterpart's
   x-position. `VolumeChart` has never worked that way - bars sit at plain
   array-index positions (`i * barWidth`) across the chart's full width,
   a deliberate existing simplification (see its own remarks, and
   `LogAggregateQueryBuilder`'s `toStartOfInterval(Timestamp, INTERVAL
   ... SECOND)`, which anchors bucket boundaries to a fixed epoch-relative
   grid rather than to the query's own `from`), and `LogAggregateResponse.Buckets`
   only contains buckets a `GROUP BY` actually matched - a real gap in one
   window doesn't necessarily line up with a gap in the other. There is no
   guarantee the current and overlay fetches return the same number of
   buckets.

## Decision

**A new `LogsFilterState.timeShiftSeconds: number | null` (dashboard-only -
no backend changes at all, same as ADR-0040), driving a second, parallel
`/api/logs/aggregate` fetch in `VolumeChart.svelte` that renders as a
dashed line independently spread across the chart's own width - not
aligned to the bars' per-bucket x-positions.**

- **No `Flare.Api` changes.** Exactly ADR-0040's reasoning: a time-shifted
  overlay is a different `LogFilter` time range sent through the same
  `aggregateLogs()` call `VolumeChart.svelte`'s `refresh()` already makes,
  not a new server-side transform. `LogAggregateRequest` is untouched.
  `shiftRange(range, seconds)` (`$lib/logs/time-range.ts`, already
  generalized by ADR-0040 for exactly this reuse) computes the offset
  window.
- **`VolumeChart.svelte`'s `refresh()`** generalizes its single
  `aggregateLogs()` call into a `Promise.all` of the current fetch plus a
  best-effort overlay fetch (`.catch(() => null)`, same "never blocks the
  main chart" shape `MetricsExplorerState.runQuery`'s overlay fetch
  established) at `shiftRange(range, timeShiftSeconds)`, using the same
  `filter`/`bucketWidthSeconds`/`postProcessFunctions` as the main fetch.
  Disabled while live (`explorer.live`): a live-tailing chart's window is a
  sliding trailing slice, so "N hours ago" would have to keep re-resolving
  every poll against a comparison that doesn't mean much yet - same
  reasoning the existing drag-to-zoom gesture already falls back to a
  plain click for while live.
- **`overlayShiftSeconds`/`overlayBuckets` are local `VolumeChart` state,
  not new `LogsExplorerState` fields** - unlike Metrics'
  `resultTimeShiftSeconds`/`previousSeries` (state, because
  `MetricsExplorerState.runQuery` is itself the fetch's call site),
  `VolumeChart.svelte` is already the one place that calls
  `aggregateLogs()` (ADR-0041's own "request-building lives in
  VolumeChart, not LogsExplorerState" decision), so the "as of the last
  fetch, not the live filter, no blink" value pairing that motivates
  `resultTimeShiftSeconds` on the Metrics side is just as easy to keep
  colocated with the fetch here.
- **No timestamp alignment - independently spread across the full chart
  width instead.** `overlayLinePoints` places overlay point `i` at
  `((i + 0.5) / overlayBuckets.length) * CHART_WIDTH`, the exact formula
  the bars already use for their own x positions
  (`i * barWidth + barWidth / 2`), computed independently against
  `overlayBuckets.length` rather than reusing the main series' `barWidth`.
  Given the codebase's existing acceptance of array-index (not real-time)
  x-positions and gap-free bucket lists, this is the simplest option that
  doesn't assume the two bucket counts match, and it degrades the same way
  the rest of the chart already does - a real gap just compresses the
  axis, exactly as documented for the bars themselves.
- **Shared y-scale.** `peakCount`/`maxCount` fold in `overlayBuckets`'
  counts whenever the overlay is active, so the dashed line and the bars
  plot on one scale - same reasoning `MetricChart`'s shared
  `domainMin`/`domainMax` gives for its own current+overlay lines.
  `overlayY(count)` mirrors `barHeight` but with no `MIN_BAR_HEIGHT` floor
  (a line's zero sits exactly on the baseline) and clamps into
  `[PEAK_Y, BASELINE_Y]` so a post-processed negative/oversized count
  (ADR-0041's own "not specially handled" consequence for the bars) pins
  to an edge rather than drawing off the visible chart.
- **Percent-change summary + hover detail**, mirroring `MetricChart`'s
  `compareChangeText`/`compareRangeDetail`: a "{percent}% vs {duration}
  ago" (or "new (no data in {duration} ago)") line next to the existing
  "N events" total, hoverable for the actual compared date ranges. The
  per-bucket hover tooltip also appends the overlay bucket at the same
  index, if one exists - the same index-based approximation the line
  itself uses, not a guaranteed exact time match.
- **Dashboard control**: `LogsTimeShiftPopover.svelte`, a near-verbatim
  copy of `MetricsTimeShiftPopover.svelte` (presets 1h/24h/7d plus a
  custom value+unit escape hatch) - same "no per-explorer gating concern
  to abstract around" reasoning `LogsFunctionsPopover.svelte`'s own
  remarks already give for copying `MetricsFunctionsPopover` instead of
  sharing one generic component. Wired into `LogsToolbar.svelte` next to
  `LogsFunctionsPopover`, before "Clear filters".
- **`LogsFilterState.timeShiftSeconds`** is a display preference on
  `VolumeChart`'s own rendering, same category `postProcessFunctions`
  already established (ADR-0041): carried through `LogsSavedViewState`/
  `toSavedViewState`/`applySavedViewState` (and therefore through
  dashboard panels too - `DashboardLogsPanelBody.svelte` reuses
  `LogsExplorerState`/`VolumeChart` wholesale, so no panel-body change was
  needed), but excluded from `hasActiveFilters()`/`resetFilters()` and
  reset (to `null`) by `applyDeepLinkFilter`, the same sticky-drill-down
  reset `postProcessFunctions` already gets there.

## Consequences

- Closes the roadmap's "Per-query post-processing functions (metrics and
  logs)" item entirely - ADR-0038/0039 (metrics functions), ADR-0040
  (metrics time-shift), ADR-0041 (logs functions), and this ADR (logs
  time-shift) now cover both explorers' full chain.
- The overlay line's x-positions are independently spread across the
  chart width rather than aligned to the bars' own per-bucket positions -
  a real v1 simplification, consistent with `VolumeChart`'s pre-existing
  "array index, not real time" x-axis and lack of bucket gap-filling. A
  shift that isn't a whole multiple of the picked bucket width, or a
  window with materially different real gaps than its comparison window,
  can visibly misalign a specific bar against the line's nearest point by
  up to roughly one bucket width - acceptable for the "roughly this time
  last week" reading this feature is for, not for a sub-bucket-precision
  claim.
- `svelte-check`/`vite build` clean. No backend changes, so no new
  `Flare.Api.Tests`. Live e2e not done, consistent with ADR-0040/0041's
  own choice not to for this same roadmap item's other slices.

## Related documentation

- `src/dashboard/src/lib/logs/time-range.ts` - `shiftRange`, reused as-is
  from ADR-0040.
- `src/dashboard/src/lib/logs/state.svelte.ts` -
  `LogsFilterState.timeShiftSeconds`, `setTimeShiftSeconds`.
- `src/dashboard/src/lib/components/logs/VolumeChart.svelte` -
  `overlayBuckets`/`overlayShiftSeconds`, `overlayLinePoints`, `overlayY`,
  `overlayChangeText`/`overlayRangeDetail`.
- `src/dashboard/src/lib/components/logs/LogsTimeShiftPopover.svelte` -
  the toolbar control.
- `docs-internal/adr/0040-metric-time-shift-overlay.md` - the Metrics-side
  time-shift decision this one mirrors.
- `docs-internal/adr/0041-log-post-processing-functions.md` - the Logs
  post-processing-chain decision that named this as its own still-open
  follow-up.
- Prior art: SigNoz's post-processing/time-shift pipeline, named in the
  original roadmap item
  ([signoz#4445](https://github.com/SigNoz/signoz/commit/3b98073ad4f0fe9825ce7e9ac47de1df16c98865),
  [signoz#4569](https://github.com/SigNoz/signoz/commit/1a62a13aeaa205cae3b474f3ad07ab2944385757),
  [signoz#4607](https://github.com/SigNoz/signoz/commit/d0d10daa442e387fe557ae0bb6c14b19d004ef8d)).
