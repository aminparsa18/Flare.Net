# ADR-0087: Stacking for the time series line chart

Status: Accepted

Date: 2026-10-03

## Context

[ADR-0086](0086-bar-chart-stacking.md) added `DashboardPanel.stacking` (`none | normal | percent`) for the
bar visualization and left the line chart out, since it lives in two components
(`MetricChart`, `FormulaChart`).

## Decision

**The same `stacking` field applies to `timeSeries`, drawn as stacked areas.**

- The band math is shared in `$lib/dashboards/stacking.ts` (`stackLines`): positives pile up,
  negatives down; percent divides each bucket by its total magnitude. A series with no point in
  a bucket contributes zero there instead of leaving a gap that would pinch the bands above it.
- Both charts stack when `stacking` is set; `MetricChart` only for Gauge/Sum series (histogram
  percentile lines and the comparison overlay are not parts of a whole).
- Stacking wins over log scale (a stack has no log reading), and percent mode drops soft Y
  bounds and thresholds, as in ADR-0086.
- No point markers on stacked areas; the tooltip lists each series' real value, with its share
  in percent mode.
- `bar` still draws its bars in `BarVisualization` rather than from `stackLines`, because bars
  are laid out per bucket.
