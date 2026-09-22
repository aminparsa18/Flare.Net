# ADR-0039: Metric smoothing post-processing functions (EWMA / median)

Status: Accepted

Date: 2026-09-22

## Context

ADR-0038 shipped the metrics-only v1 slice of the roadmap's "Per-query
post-processing functions (metrics and logs)" item: point-wise
clamp-min/max/absolute/log2/log10 plus a running cumulative-sum, all via
`MetricPostProcessFunction { Type, Value }`. Its Consequences section
explicitly named smoothing (EWMA/median-over-N) as a still-open follow-up,
not an oversight: "it needs a window/lookback parameter this chain shape
doesn't have yet, a real (if small) design question of its own."

This ADR picks up that follow-up. Time-shift (re-run a query at an offset,
overlay the result) and Logs support remain separate, still-open follow-ups
- see ADR-0038's Consequences for why time-shift in particular is
structurally different (a second query dispatch plus a result-alignment
step, not a single-pass transform).

## Decision

**Two new `MetricPostProcessFunctionType` members, `EwmaSmoothing` and
`MedianSmoothing`, appended after `CumulativeSum` (ordinals 6/7, preserving
every existing ordinal per the enum's own MemoryPack-versioning remarks),
plus a new `MetricPostProcessFunction.WindowSize` (`int?`) field appended
after `Value` (MemoryPack's 3rd field, same append-only convention
`WindowSize` itself now establishes for whatever's added next).**

- **`WindowSize` is a bucket count, not a duration** - required (`>= 1`)
  for the two smoothing functions, ignored (may be null) for every other
  function, the same "required for some members, ignored for others"
  shape `Value` already has for `ClampMin`/`ClampMax`. A bucket count
  rather than a wall-clock duration keeps the parameter meaningful
  regardless of the query's `bucketWidthSeconds`, and needs no unit
  conversion in `MetricPostProcessor`.
- **`EwmaSmoothing`**: converts the N-period window into a decay factor via
  the standard `alpha = 2 / (N + 1)` formula, then runs the textbook
  recurrence `ewma[i] = alpha * value[i] + (1 - alpha) * ewma[i-1]`. A
  window of 1 degenerates to `alpha = 1` (no smoothing, passthrough) -
  a valid, if pointless, input; not special-cased.
- **`MedianSmoothing`**: the median of the non-null values in a *trailing*
  window of up to `WindowSize` buckets ending at (and including) the
  current one - causal, not centered, so it never looks ahead of the
  current bucket. Same reasoning ADR-0038's cumulative-sum only ever runs
  forward.
- **Null semantics deliberately diverge from ADR-0038's point-wise
  null-in/null-out rule**: gap-filling small holes is the actual value
  proposition of a smoothing function, so a null bucket doesn't stay null
  in the output as long as real data exists nearby. `EwmaSmoothing` skips
  updating its running average on a null input but still emits whatever
  average has accumulated so far (carries forward, same "don't re-puncture
  gaps" idea as cumulative-sum's own deviation from the point-wise rule).
  `MedianSmoothing` simply excludes null points from the trailing window
  rather than letting one gap blank the whole window's median. Both emit
  null only when no real data has been seen at all yet (EWMA: before the
  first real value; median: when the trailing window is entirely empty).
- **Dashboard**: `MetricsFunctionsPopover.svelte` gained two more
  `FUNCTION_OPTIONS` entries and a `needsWindowSize()` predicate parallel
  to the existing `needsValue()`, swapping in a window-size `Input` in
  place of the clamp threshold `Input` for those two types. Switching a
  row's type into a smoothing function pre-fills a `DEFAULT_WINDOW_SIZE`
  (5) the first time, rather than leaving the input blank - the same
  "don't force the user to know a magic number" reasoning as any other
  sensible default in this codebase, not a backend requirement (the
  backend rejects `WindowSize < 1`, nothing more). No other file changed:
  `MetricsFilterState.postProcessFunctions` (state.svelte.ts) already
  carries an opaque `MetricPostProcessFunction[]`, and `MetricChart.svelte`
  already just renders whatever `series` it's given.
- **Wire format**: `WindowSize` is a plain `int?`, so `[GenerateTypeScript]`
  regenerates `MetricPostProcessFunction.ts`/`MetricPostProcessFunctionType.ts`
  automatically on `dotnet build` - no new hand-written mirror needed
  beyond the existing `enums.ts` name-array (`METRIC_POST_PROCESS_FUNCTION_TYPE_NAMES`),
  same one ADR-0038 already required for the first six members.

## Consequences

- Time-shift (re-run a query N seconds earlier, overlay the result) is
  still the one open item from the original roadmap bullet, plus extending
  any of this (point-wise, running, or smoothing) to the Logs explorer.
  Neither is touched here.
- `WindowSize` reuses ADR-0038's exact append-only MemoryPack field
  convention, so no wire-format migration concern: an old dashboard build
  talking to a new backend (or vice versa) already falls back correctly
  via the generated code's `count`-based versioning path.
- 877/877 `Flare.Api.Tests` (10 new `MetricPostProcessorTests` cases),
  `svelte-check` clean.

## Related documentation

- `src/Flare.Api/Model/MetricModels.cs` - `MetricPostProcessFunctionType.EwmaSmoothing`/
  `MedianSmoothing`, `MetricPostProcessFunction.WindowSize`.
- `src/Flare.Api/Query/MetricPostProcessor.cs` - `ApplyEwmaSmoothing`/
  `ApplyMedianSmoothing`, and this class' own remarks on why their null
  semantics diverge from the point-wise functions.
- `src/dashboard/src/lib/components/metrics/MetricsFunctionsPopover.svelte` -
  `needsWindowSize`/`DEFAULT_WINDOW_SIZE`.
- `docs-internal/adr/0038-metric-post-processing-functions.md` - the v1
  scope decision and the point-wise/running functions this ADR extends.
