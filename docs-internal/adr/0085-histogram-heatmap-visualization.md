# ADR-0085: Heatmap visualization for histogram metrics

Status: Accepted

Date: 2026-10-03

## Context

[ADR-0061](0061-dashboard-histogram-and-column-units.md)'s `histogram` visualization
pools every per-bucket reading into one distribution for the whole range. For a
latency metric the usual question is how that distribution *moves*: a second slow
mode appearing, the body drifting up. Percentile lines hide it. A heatmap (time
along X, value range along Y, color for observation count) answers it.

The data was not on the wire. `MetricQueryService` has the per-time-bucket bucket
arrays (`sumForEach` for explicit histograms, merged per-scale slices for
exponential ones, [ADR-0060](0060-exponential-histogram-metrics.md)) but reduces
them to count, sum and percentiles before returning a `MetricSeriesPoint`. Four
things needed a decision:

1. How per-bucket counts reach the dashboard.
2. What shape they take, given explicit and exponential histograms differ.
3. How buckets land on the heatmap's rows.
4. Whether the color scale is a saved panel option.

## Decision

**An opt-in `IncludeBuckets` request flag; when set, each histogram point also
carries its non-empty buckets as three parallel lists. A `heatmap` visualization
draws them.**

- **Opt-in.** `MetricQueryRequest.IncludeBuckets` (appended last, nullable, like
  `TreatAsCounter`). Without it the response is unchanged. The list lengths
  multiply the payload by the bucket count, and only the heatmap uses them. It is
  part of the request, so the query cache key carries it with no extra work.
  `DashboardMetricsPanelBody` sets it from the panel's visualization and re-runs
  the query when the visualization changes into or out of `heatmap`, the one
  visualization switch that fetches.
- **One shape for both histogram kinds.** `MetricSeriesPoint.BucketLowers`,
  `BucketUppers`, `BucketCounts` (parallel `double` lists, ascending, empty buckets
  dropped). `HistogramBucketExpander` produces them. Exponential buckets are exact
  (`(base^i, base^(i+1)]`, negatives mirrored, the zero bucket spanning
  `±ZeroThreshold`). Explicit histograms have open ends that a cell can't draw: the
  first bucket is closed at `min(0, bounds[0])` (latency-like data is non-negative)
  and the overflow bucket one bucket-width past the last bound. Counts are `double`
  so the hand-written TypeScript reader needs no `bigint` handling.
- **Shared rows, built client-side.** `heatmapGrid` pools every series per time
  bucket, as `histogram` does. If there are at most 40 distinct bucket edges they
  become the rows, so a classic explicit histogram gets exact per-bucket counts. Past
  that (exponential histograms, whose edges differ by scale) the range is cut into 40
  rows, log-spaced when all positive, and each bucket's count is split across the rows
  it overlaps in proportion to the overlap (in log space for log rows).
- **Color scale is a local toggle, not a panel field.** Counts are heavy-tailed, so it
  starts logarithmic; a control under the chart switches to linear. Saving it would
  need a new `DashboardPanel` field, parser and editor for a presentational choice
  nobody has asked to persist.
- Grafana `heatmap` panels import as `heatmap`. Non-histogram metrics (and formula
  panels) show a message instead of an empty chart.

## Consequences

- Heatmap panels cost more per query than other visualizations; no other panel pays.
- Rows are ordinal for explicit histograms, so a bucket's visual height doesn't
  reflect its value width. That is how Prometheus-style bucket heatmaps read, and
  keeps counts exact rather than smeared.
- Per-series separation is not possible: series are pooled.
- The explicit-histogram open-end closing is a presentation choice; the first cell
  starts at 0 even if the instrument could in principle record negatives.
