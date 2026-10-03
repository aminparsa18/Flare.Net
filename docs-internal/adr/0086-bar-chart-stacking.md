# ADR-0086: Stacking option for the bar visualization

Status: Accepted

Date: 2026-10-03

Partially supersedes [ADR-0059](0059-dashboard-panel-visualizations.md): `stackedBar` is no longer a
separate `visualization` value.

## Context

ADR-0059 shipped `stackedBar` as its own visualization, stacking in absolute values only.
There was no way to see each series' share of a bucket (100% stacking), and a separate
visualization per layout would have meant a `percentStackedBar` as well. SigNoz added the
same option ([signoz#12632](https://github.com/SigNoz/signoz/commit/485aed0e1ae0928661f452df6ad058331cd5501a)).

## Decision

**Stacking is a separate optional `DashboardPanel.stacking` field (`none | normal | percent`)
read by the `bar` visualization; `stackedBar` is retired.**

- `percent` rescales each bucket to its total magnitude, so every stack fills 0-100% and the
  Y axis is labelled in `%`. Negative values keep their sign and hang below zero, with the
  denominator being the sum of absolute values. The tooltip still shows each series' real value,
  with its share in brackets.
- Soft Y bounds and thresholds are expressed in the metric's unit, so they are ignored in
  percent mode rather than being reinterpreted.
- **No migration of saved panels.** `parseVisualization` reads a stored `stackedBar` as `bar`
  and `parseStacking` reads it as `normal`; the viewer rewrites the panel to `bar` + `normal`
  the next time that panel's visualization or stacking is saved. Layouts are opaque JSON in
  `layoutJson`, so no backend or schema change.
- The time series line chart is not covered: stacked areas would need changes to both
  `MetricChart` and `FormulaChart`, tracked in the roadmap.

## Consequences

- Old dashboards and exports containing `stackedBar` keep working unchanged.
- Re-importing a new dashboard into an older build shows `bar` side by side, since the old
  build doesn't know `stacking`.
