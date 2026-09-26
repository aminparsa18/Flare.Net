# ADR-0061: Value-distribution histogram visualization and per-column table units

Status: Accepted

Date: 2026-09-26

## Context

[ADR-0059](0059-dashboard-panel-visualizations.md) gave Metrics panels six
visualizations and left two follow-ups on the roadmap: a value-distribution
histogram ([signoz#4858](https://github.com/SigNoz/signoz/commit/7e9bf2d48da640b7203e4cd19cdf91575dedfde2))
and a per-column unit override for the table
([signoz#5134](https://github.com/SigNoz/signoz/commit/2145e353c81ab22ef60b09e4f71b8917a3f16709)).
Both are frontend-only and fit ADR-0059's frame. Four things needed a
decision:

1. What the histogram counts.
2. How bins are chosen.
3. Whether series stay separate in the histogram.
4. How a column unit is stored and what it means.

## Decision

**A seventh `visualization` value, `histogram`, that pools every series'
per-bucket readings into one distribution. A `columnUnits` map on
`DashboardPanel`, keyed by reducer, that says what unit a table column's raw
number is in.**

- **The histogram counts per-bucket readings**, the same one-number-per-bucket
  value ADR-0059 defined for every other visualization (a Histogram metric's
  bucket mean, a Gauge/Sum value, a formula result). It's the distribution of
  what the chart would plot, not of individual observations. A Histogram
  metric's own OTLP bucket counts would give the true observation
  distribution, but `MetricSeriesPoint` doesn't return them, and exposing
  them is an API change well beyond this follow-up.
- **Bins are round numbers, sized by reading count.** Edges are
  `niceAxisTicks` over the data range in the display scale, so they read
  "0 / 50 / 100 ms". The target count is Sturges' rule (ceil(log2 n) + 1),
  clamped to 5..30. No user-set bin count or width: nothing in practice has
  asked for one, and it can be added later as another optional field.
- **Series are pooled, one color.** The question a histogram answers is "what
  values does this query take". Per-series overlaid or stacked bins on a
  shared axis read badly past two or three series. Thresholds apply to the
  bins by midpoint, since values are the X axis here.
- **`columnUnits` declares a column's unit, it doesn't convert.** It is
  `Partial<Record<PanelReducer, string>>`, stored in the opaque `LayoutJson`
  like `reducer`, read through the lenient `parseColumnUnits`. A column's
  unit is the UCUM string `resolveAxisScale` then scales from, so `ms` on a
  raw 1500 reads "1.5 s". It is a relabelling, not a unit conversion from the
  metric's unit. Its main uses are a Formula panel, whose result has no unit,
  and a metric that declares the wrong unit or none at all. Converting
  between two declared units was not built: the table's columns all come
  from one metric, so a conversion would have one source unit per panel, and
  the Y-axis/tooltip path has no conversion concept either.
- **Grafana `histogram` panels map to it** on import, same as ADR-0059's
  other nearest-visualization mappings.

## Consequences

- No backend change or migration. A dashboard saved with `histogram` or
  `columnUnits` and opened in an older build falls back to the line chart /
  the metric's unit.
- The column-unit editor lists all five reducer columns, including Sum,
  which only appears for Sum metrics; an override for a hidden column is
  kept but unused.
- CSV downloads stay raw values with no unit, so a column override doesn't
  change them.
- A true observation-level distribution for Histogram metrics (from their
  bucket counts) is not planned; it would need `MetricSeriesPoint` to carry
  bucket boundaries and counts.
