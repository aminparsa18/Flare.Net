# ADR-0059: Dashboard panel visualizations - a per-panel draw mode over an unchanged query

Status: Accepted

Date: 2026-09-26

## Context

`DashboardPanel.panelType` (ADR-0023) names a panel's *data source* - which
Explorer's saved query it embeds - and each type has exactly one rendering:
Logs is the event-volume chart, Traces the trace list, Metrics a line chart
(`MetricChart`, or `FormulaChart` in formula mode). There was no bar, pie,
single-value or table rendering, and no way to change how a panel looks
without rebuilding it. The roadmap tracked this with SigNoz prior art: a pie
chart panel ([signoz#4751](https://github.com/SigNoz/signoz/commit/a54b7baa7d4754fb752cc61a048f2f8ff167241c)),
changing a panel's type in place ([signoz#4759](https://github.com/SigNoz/signoz/commit/6815a96d29e1c6ca0059621bf56b2949f7af378a)),
stacked bars ([signoz#5138](https://github.com/SigNoz/signoz/commit/f2aba5035a2f106be45848e5eee9e012da6ed5f4)),
table CSV download, sortable columns and in-table search
([signoz#5067](https://github.com/SigNoz/signoz/commit/76b1e40cbc2182165abbb538f32481265bd35b75),
[signoz#5114](https://github.com/SigNoz/signoz/commit/0760917a4b54bf6629a5c08d02201407797d00bf),
[signoz#5893](https://github.com/SigNoz/signoz/commit/cb1cd3555b3b63bdb441512dacdebf2599db67d7)),
and units on pie values ([signoz#5960](https://github.com/SigNoz/signoz/commit/3573c0863c59711d48b28d91d4d775dbc4929666)).

Things needing a decision:

1. Where the visualization lives, and whether it's a new `panelType`.
2. Which panel types get alternatives.
3. How a series set collapses to one number (value, pie, table), given
   Gauge, Sum, Histogram and formula results mean different things.
4. Whether switching visualization re-runs or changes the query.

## Decision

**A separate, optional `visualization` field on `DashboardPanel`, Metrics
panels only, rendered client-side from the result the panel's explorer has
already fetched. A second optional `reducer` field says how a series becomes
one number.**

- **Separate field, not new panel types.** `visualization` is one of
  `timeSeries | bar | stackedBar | value | pie | table`; `undefined` (and any
  unrecognised value) means `timeSeries`, so every existing dashboard renders
  as before. It lives inside the opaque `LayoutJson` like `thresholds` and
  `yAxisMin` - Flare.Api never interprets it, so there's no backend change and
  no migration. Keeping `panelType` as "data source" means the "Create alert"
  draft, variables, time-range override and export keep working unchanged:
  none of them care how a panel is drawn.
- **Metrics only.** Logs and Traces bodies aren't numeric series sets - a
  trace list has nothing to pie-chart - so they have no menu.
- **Switching never touches the query.** `DashboardMetricsPanelBody` runs the
  same `MetricsExplorerState` query path whatever the visualization and only
  swaps which component reads `series`/`formulaSeries`. Changing
  visualization is one `#saveLayout` PUT, the same path thresholds use.
- **One number per bucket, then one reducer.** Gauge/Sum use `value`; a
  Histogram bucket uses its mean (`sum / count`), because it's the one
  Histogram reading that stays meaningful when then averaged/min/maxed across
  buckets (an average of p95s is not a p95); a formula result is read like a
  Gauge. `reducer` is `last | avg | sum | min | max`; absent means `sum` for a
  Sum metric (its buckets are increments) and `avg` for everything else
  (levels, where summing scales with bucket count). A Value panel over
  several series sums them per bucket first, the same collapse MetricChart's
  comparison overlay uses.
- **Raw values, not the line chart's view modes.** Bars and numbers use raw
  Sum increments, not MetricChart's default per-second Rate, and not its
  Histogram percentile modes. Those modes are MetricChart-internal session
  state, not part of the panel; reproducing them per visualization would
  multiply the option surface for little gain.
- **Palette discipline carries over.** Bars cap at the 5-colour categorical
  palette (the 5 largest series by magnitude); pies color by rank and fold
  past the 4th slice into "Other". The hash-based `seriesColor` moved to
  `$lib/metrics/chart-colors.ts`, shared by all three chart components.
- **Grafana import keeps the nearest visualization** (stat/gauge/bargauge ->
  value, barchart -> bar, piechart -> pie) instead of flattening them all to
  a line chart.

## Consequences

- Existing dashboards are untouched; a dashboard saved with a visualization
  and opened in an older build just renders the line chart (the field is
  ignored), which is the right failure mode.
- Thresholds apply to every visualization except pie (a Value number and
  table cells take the matching rule's color). Soft Y-axis bounds only apply
  to line/bar, so the Y-axis popover is hidden for the others. Bars always
  anchor at zero, so a positive `yAxisMin` can't raise their floor.
- Table sort and search are session-only view state, not saved.
- Not built, left on the roadmap: a per-column unit override for tables and
  a value-distribution histogram visualization. A Logs panel "value" (total
  event count) would need a Logs-side reducer and is not planned.
