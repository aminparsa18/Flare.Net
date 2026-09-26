# ADR-0060: OTLP ExponentialHistogram metrics, and temporality-aware histogram queries

Status: Accepted

Date: 2026-09-26

## Context

Migration 0008 stored three of OTLP's five metric point types (Gauge, Sum,
Histogram) and deliberately dropped ExponentialHistogram and Summary, under the
"add it when a concrete need exists" precedent Span Links also followed. .NET's
OpenTelemetry SDK emits an exponential histogram whenever an app opts an
instrument into `Base2ExponentialBucketHistogramConfiguration`, a standard
`Meter` view. Those points reached Flare.Ingest and were thrown away, with only
an Ingest-side warning log to say so. Nothing upstream blocked support: the
vendored OTLP proto already defines the type, and OpenTelemetry .NET 1.19
emits it.

An exponential data point carries no bucket bounds. It has a `scale`: bucket
`i` covers `(base^i, base^(i+1)]` with `base = 2^(2^-scale)`. It also has an
offset plus counts for the positive and negative ranges, a zero bucket
(`zero_count`, `zero_threshold`), and optional `min`/`max`. The SDK lowers
`scale` on its own as the observed range widens (at most 160 buckets per
range), so two points of the same series can have different bucket layouts.
Live-run evidence: one series moved from scale 7 to scale 3 when a 60000 ms
outlier arrived.

Things needing a decision:

1. Store in `metrics_histogram` (converted to explicit bounds), or a new table.
2. Where rows with different scales get merged: SQL or C#.
3. How the API and dashboard expose the type.
4. Whether to fix a flaw found in the existing Histogram queries while here: they
   summed `Count`/`BucketCounts` across a time bucket's rows regardless of
   `AggregationTemporality`. That's right for delta points, but a cumulative point
   (the .NET SDK's default, and every Prometheus-scraped histogram) is a running
   total, so summing N of them counted the history N times. Live evidence: a
   cumulative series whose counts ran 101 → 3306 over eight exports reported 14139.

## Decision

**A new `metrics_exponential_histogram` table (migration 0032), stored in the
wire's own shape. The query groups by `Scale` and adds buckets by absolute
index with `sumMap`, and a pure C# `ExponentialHistogramEstimator` downscales
per-scale slices to the lowest scale, merges them, and estimates percentiles.
A new `MetricPointType.ExponentialHistogram` returns exactly Histogram's
point shape, so every consumer treats it as a histogram.**

- **Own table, not conversion.** Converting each point to explicit bounds
  would give each scale a different bound set. `metrics_histogram`'s
  `sumForEach(BucketCounts)` adds arrays position by position, assuming one
  layout per metric (see `HistogramQuantileEstimator`'s remarks), so it would
  silently add unrelated buckets together. The new table keeps `Scale`,
  offsets and counts. It also stores `Min`/`Max` as `Nullable(Float64)`:
  unlike `Sum` they have no neutral default, the .NET SDK does send them, and
  they give a real max plus a clamp for estimates in the outermost bucket.
- **SQL adds up per scale, C# merges across scales.** The chart and alert
  queries share `MetricSeriesQueryBuilder.ExponentialHistogramAggregates`:
  `sumMap(arrayMap(i -> toInt32(Offset + i - 1), arrayEnumerate(counts)), counts)`
  keys counts by absolute bucket index, so rows with different offsets add
  correctly, and `GROUP BY ..., Scale` keeps each scale separate. That usually
  returns one row per group, occasionally two around a rescale. Downscaling in
  SQL would need a window function to find each group's lowest scale first. It
  would also rely on ClickHouse's shift semantics for negative indices, which
  the "no fake ClickHouse in unit tests" convention couldn't cover. The C#
  merge is an arithmetic right shift (floor division, correct for negative
  indices) and is fully unit-tested.
- **Log-scale interpolation.** `Estimate` walks negative buckets (largest
  magnitude first), then the zero bucket, then positive buckets, and
  interpolates inside the target bucket as `base^(i + fraction)`, which matches
  how exponential bucket widths grow. The result is clamped to the observed
  `Min`/`Max`. `EstimateMax` returns the real `Max` when one was sent.
- **Same point shape, new enum member.** `MetricSeriesPoint`'s
  count/sum/p50-p99/`MaxApprox` fields carry ExponentialHistogram results
  unchanged. The dashboard's histogram checks go through one
  `isHistogramType()` helper, so the percentile/mean/max views, comparison
  overlays, alert aggregations, formula-picker exclusion and dashboard reducers
  behave the same for both kinds. The CLI maps the type onto its Histogram
  rendering. The type stays distinct in the API (not folded into
  `Histogram`) because it picks the table: folding them would mean every
  histogram query unions both tables with two incompatible row shapes.
- **Both histogram kinds are temporality-aware** (`HistogramTemporalitySql`),
  following the Sum precedent (ADR-0035/ADR-0044): a `row_number()`/`lagInFrame()`
  window over the full (`ServiceName`, `toString(DataPointAttributes)`) series
  identity. Delta rows count as-is. A series' first cumulative row in range
  contributes nothing. A reset contributes the row itself: a changed `StartTime`
  (OTLP's reset signal), a lower `Count`, or, for explicit buckets, a changed bucket
  layout. Any other cumulative row contributes its difference from the previous row.
  Explicit buckets diff element-wise. Exponential rows can't, because the previous
  row may be at another scale, so a cumulative exponential row emits two signed
  contributions: itself (+1) and the previous row (−1), each at its own scale. The
  C# downscale-merge cancels them exactly, since downscaling is linear, and clamps
  whatever stays negative. A cumulative row's `Min`/`Max` cover the series' whole
  lifetime rather than the queried range, so they're dropped and the max falls back
  to the bucket-boundary estimate.
- **Summary stays unsupported.** It holds precomputed quantiles that can't be
  merged across series or time buckets. It is still dropped with the existing
  warning log.

## Consequences

- Real .NET exponential histograms now show up in the Metrics explorer,
  dashboards, `flare metrics`/`flare metric`, and metric-threshold alerts
  (Count, Sum, P50-P99, MaxApprox).
- Accuracy is bounded by the lowest scale merged into a bucket: in the live
  run, merging scale-7 and scale-3 rows reported p99 as 1009 ms against a true
  990 ms (a scale-3 bucket is about 9% wide). p50/p75/p90 matched the true
  values to within 1 ms.
- Cumulative histograms of both kinds now report the true increase. In the live
  run, the cumulative series above reported 3205 (= 3306 − 101) with p50 466.8 ms,
  matching the recorded values. Per-second buckets showed each export's exact
  increase (101, 101, 1001, 1001, 1001), including across the 7 → 3 rescale.
  Delta series are unchanged. This fixes existing explicit-bucket and
  Prometheus-scraped charts and alerts as well, not just the new type.
- Same trade-off as Sum: the first point of each series in the queried range has
  no predecessor, so its content isn't counted. A short range therefore under-counts
  by one export interval instead of over-counting by the whole history.
- The top-N series ranking still uses raw `sum(Count)`. It only decides which
  series make the cut, the same looser standard Sum's ranking uses.
- Migration 0032 is additive (a new table). Existing data and queries are
  unaffected.
