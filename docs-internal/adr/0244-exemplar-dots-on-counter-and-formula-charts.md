# ADR-0244: Exemplar dots on counter, gauge and formula charts

Status: Accepted

Date: 2026-10-18

Extends [ADR-0242](0242-dashboard-exemplar-dots.md), which drew dots on histogram charts only.

## Context

ADR-0242 left counters and formulas without dots because their exemplar is not on the chart's axis. A histogram's
exemplar is one observation (a 1.8 s request) on the same axis as the p50/p99 lines, so it sits at its value. A
counter's exemplar is the increment that was added (usually 1 or a byte count) while the chart plots a rate or a count
per bucket, and a formula's value is a ratio or difference of several metrics. Placing either at its own value would
put it at an arbitrary height. But a counter spike (errors per second) is exactly where "show me a request that did
this" is wanted.

## Decision

- **A dot for a counter, gauge or formula rides the line of the series it was recorded on**: x is the exemplar's own
  time, y is the drawn line's height at that x, interpolated along its straight segments (`yOnLine`), so the dot sits
  on the line rather than floating at the containing bucket's height. The title carries the exemplar's real value, in the
  metric's unit (dimensionless for a formula).
- **Which series**: same service, and every attribute the series is keyed by equal on the exemplar
  (`exemplarOnSeries`): all of the point's attributes when ungrouped, the single group-by key when grouped, a missing
  key reading as the empty "no value" series. An exemplar that matches no drawn line gets no dot: a series folded into
  "+N not shown", one hidden by click-to-isolate, or an exemplar of a series the formula's join dropped.
- **No dots in stacked mode or in a comparison overlay**: in a stack the plotted height is a cumulative layer, and an
  overlay line is a total over every series, so neither is a line a dot can honestly sit on.
- **Histograms are unchanged** (own value, clamped to the plot edge), and the shared ring-and-link markup moved to
  `ExemplarDots.svelte`, used by `MetricChart` and `FormulaChart` alike.
- **A formula reads the exemplars of every metric it references**, one request per distinct (metric, type, service) in
  the formula's own window. The source list is keyed by identity, so editing a row's group-by or the arrival of its
  attribute keys does not refetch. The same 100-dot cap applies to the combined list.
- `exemplars` is set on the Metrics page and on dashboard Metrics panels, for both chart components.

## Alternatives considered

- **Own value on a second, hidden axis.** Rejected: an axis nobody can read, for values that are only meaningful as "this
  request is in that bucket".
- **A strip of dots along the bottom edge.** Rejected: loses the link between the dot and the spike it explains, which
  is the point of the feature.
- **Snap to the nearest line when nothing matches.** Rejected: a dot on the wrong series would send you to a trace that
  has nothing to do with the line it appears on.

## Consequences

- Only the metric's own exemplars are shown; an exemplar of a series the chart does not draw is not.
- An exemplar before a line's first point (or after its last, bar the newest bucket, which is pinned to the right edge)
  has no segment to ride and is not drawn.
