# ADR-0035: Cumulative-counter rate()/increase() via windowed per-row deltas

Status: Accepted

Date: 2026-09-21

## Context

`docs-internal/planning/roadmap.md` flagged that `MetricSeriesQueryBuilder`'s
Sum-type value expression - `max(Value) - min(Value)` per bucket - was a
named, deliberately-unresolved v1 approximation with two real correctness
gaps, both called out in that file's own remarks:

- It never branched on `AggregationTemporality`. OTLP Sum points can be
  reported as `AGGREGATION_TEMPORALITY_DELTA` (each point already *is* the
  increase since the last report - the correct per-bucket value is
  `sum(Value)`) or `AGGREGATION_TEMPORALITY_CUMULATIVE` (each point is a
  running total - the correct per-bucket value is the *increase* across the
  bucket, not the raw total). Applying `max - min` to a delta-temporality
  series is simply wrong, not an approximation of anything.
- Even restricted to the cumulative case it claims to approximate, `max -
  min` reads a counter reset (a process restart, the overwhelmingly common
  .NET case for `System.Runtime`/`ASP.NET Core` instrumentation) as a *dip*
  inside the bucket where the reset happened, understating the true increase
  for that bucket, sometimes into a wrongly-negative value.

`MetricChart.svelte` already exposed a "Rate" view mode (default for Sum
metrics) that divides the returned `Value` by the bucket width - client-side,
on already-fetched data. That view is only as correct as the `Value` it
divides; fixing the division was never the gap, the underlying per-bucket
number was.

The named prior art in the roadmap item was SigNoz's `WINDOW ... PARTITION BY
<series> ORDER BY ts` + `lagInFrame` pattern: compute each raw row's delta
from its immediately-preceding row via a window function (not a running
`ClickHouse`-order-dependent `runningDifference`, which silently breaks
across partitions/parts where row order isn't guaranteed), then sum those
per-row deltas into a bucket.

## Decision

**`MetricSeriesQueryBuilder`'s Sum branch (`BuildSumSql`) computes a per-bucket
`increase()` via a `WITH ranked AS (...)` CTE + window functions, replacing
the flat `max(Value) - min(Value)` GROUP BY. Gauge/Histogram are unchanged
(`BuildSimpleSql`, the original flat shape, split out unchanged).**

Concretely, the CTE selects raw `metrics_sum` rows (after the same
`WHERE`/top-N-series filter as before) and computes, per row:

- `row_number() OVER (PARTITION BY ServiceName, toString(DataPointAttributes)
  ORDER BY Time) AS SeriesRowNum`
- `Value - lagInFrame(Value) OVER (PARTITION BY ServiceName,
  toString(DataPointAttributes) ORDER BY Time) AS RawDelta`

**Critically, the window's `PARTITION BY` always uses the full, ungrouped
`toString(DataPointAttributes)` - never
`MetricQueryRequest.GroupByAttributeKey`'s collapsed `SeriesKey`.** A
counter's actual identity (what it independently resets on process restart)
is its complete attribute set, not whichever single key a chart happens to
be grouping by. Windowing over the collapsed key would interleave two
unrelated counters' raw values by timestamp and diff across them - live-
verified as a real bug during this change (see Consequences), not a
theoretical one.

The outer query then classifies each row with one `multiIf` and sums the
result per `(BucketStart, ServiceName, SeriesKey)`:

1. `AggregationTemporality = 'AGGREGATION_TEMPORALITY_DELTA'` → `Value` as-is
   (already a delta).
2. `SeriesRowNum = 1` → `0` (no preceding row in the requested window to diff
   against; counting `Value` itself here would overcount the first bucket by
   the counter's entire lifetime-so-far).
3. `IsMonotonic = 0` (an OTel UpDownCounter) → the raw `RawDelta`, negative or
   not. A legitimate decrease isn't a reset for a counter that's allowed to
   go down.
4. `RawDelta < 0` (remaining case: monotonic, cumulative) → `Value` in place
   of the delta - the reset-compensation heuristic Prometheus/SigNoz's
   `lagInFrame` pattern uses (the counter restarted at/near zero, so its
   current value approximates the increase since the reset).
5. Otherwise → the plain `RawDelta`.

`MetricChart.svelte`'s existing "Rate" mode (`value / bucketWidthSeconds`)
needed no change - it was already the correct shape for turning a per-bucket
increase into a rate, it just needed a correct increase to divide.

## Alternatives considered

- **Branch only on `AggregationTemporality`, keep `max - min` for the
  cumulative case.** Fixes half the named gap (delta metrics) but leaves the
  counter-reset dip un-addressed - the more commonly-hit problem in practice
  (every service restart). Rejected: the roadmap item explicitly named both,
  and the windowed approach isn't meaningfully more complex once a CTE is
  needed anyway.
- **`runningDifference()` instead of `lagInFrame` + explicit `ORDER BY`.**
  This is exactly what the roadmap item's cited prior art rejected:
  `runningDifference` depends on the incidental row order ClickHouse
  processes a query in, which isn't guaranteed across parts/partitions -
  it can silently produce wrong deltas with no error. `lagInFrame` inside an
  explicit `ORDER BY Time` window has no such dependency.
- **A new `MetricQueryRequest.Aggregation` (Sum/Rate/Increase) field,
  computed differently per mode server-side.** Rejected: the client-side
  Rate view was already a correct reshape of a correct increase - adding a
  parallel server-side mode would duplicate logic for no behavioral gain,
  and would touch the wire contract for a change that's really about fixing
  what `Value` already means.

## Consequences

- Sum-type queries are no longer a single flat `GROUP BY` - they're a CTE
  with two window functions per row, then a second aggregation pass. More
  work per query than before, though still bounded by the same top-N-series
  pre-filter and ClickHouse execution caps every query in this codebase
  already sets.
- Live-verified against a real ClickHouse instance (this project's own "no
  fake ClickHouse in unit tests" convention - window functions/CTEs were
  new SQL shapes for this codebase, unlike the `IN`-tuple bug a prior pass
  caught the same way): a synthetic monotonic-counter reset, a delta-
  temporality series, and a non-monotonic (`IsMonotonic = 0`) series all
  produced the expected values. Grouping by a collapsed key while two
  distinct raw series shared it was also verified - partitioning by the
  collapsed key instead of the full attribute map (the naive first attempt)
  produced a materially wrong total (250 instead of the correct 60 for two
  counters increasing by 10 and 50); partitioning by the full
  `toString(DataPointAttributes)` fixed it. This confirms the "always
  window over the full identity" rule above isn't just defensive - it's
  load-bearing.
- `MetricAlertConditionQueryBuilder` (ADR-0020's metric-threshold alerting)
  deliberately keeps the old `max(Value) - min(Value)` for now - it's a
  separate builder (a single whole-window scalar, not a bucketed chart), and
  this roadmap item was scoped to the metrics explorer. It has the same
  counter-reset blind spot this ADR fixes for the chart; left open as a
  distinct, named gap rather than silently ported alongside.
- No wire-contract change: `MetricQueryRequest`/`MetricSeriesPoint` are
  unchanged. Existing dashboard code (`MetricChart.svelte`'s Sum/Rate/Count
  modes) needed no changes - it already treated `Value` as "the bucket's
  aggregate, divide by width for a rate," which is what it now correctly is.

## Related documentation

- `src/Flare.Api/Query/MetricSeriesQueryBuilder.cs` - the `remarks` doc
  comment (updated alongside this ADR) and `BuildSumSql`.
- `docs-internal/adr/0020-metric-threshold-alerting.md` - the alert-condition
  builder this ADR deliberately doesn't touch.
- `db/clickhouse/0008_metrics.sql` - `AggregationTemporality`/`IsMonotonic`
  columns this query reads.
