# ADR-0223: `histogram_over_time()` and exemplars for TraceQL metrics

Status: Accepted

Date: 2026-10-15

Extends [ADR-0221](0221-traceql-metrics.md) and [ADR-0222](0222-traceql-metrics-over-an-attribute.md), which
rejected `histogram_over_time()` and accepted `with(exemplars=...)` while returning none.

## Context

Tempo's shapes were checked against its source and against Grafana 13.2.4 driven through a stub server and then
through Flare, not assumed:

- `histogram_over_time(v)` returns one series per power-of-two bucket, each with a label `__bucket` whose value is a
  float (`doubleValue`) and the bucket's upper bound, the value rounded up to the next power of two after being
  truncated to an integer (`Log2Bucketize`; below 2 is dropped). For a duration the bound is in seconds.
  Each sample is the count of spans in that bucket in that step.
- Grafana's Tempo data source recognises the `__bucket` label: the frames come back with
  `preferredVisualisationPluginId: heatmap`, and Explore draws a heatmap (checked with a screenshot).
- Grafana asks for exemplars with `exemplars=N` on `/api/metrics/query_range` (the editor's "Exemplars" option), and
  turns each `series[].exemplars[]` (`labels`, `value`, `timestampMs`) into an annotation frame. It reads the
  trace id from the label `trace:id` (shown as `traceId`, "Trace ID").

## Decision

### `histogram_over_time`

- **`histogram_over_time(duration)` and `histogram_over_time(span.x)`, with `by (...)`.** The attribute is read as in
  ADR-0222. Buckets are `toUInt8(ceil(log2(toUInt64(v))))` over spans whose value is at least 2 (and below 2^63), so
  the bounds are exact powers of two (checked for 2^2 to 2^62), as Tempo's.
- **One series per group and bucket, labelled `__bucket`** with the bound as a `doubleValue`: in seconds for a
  duration (`0.001048576` for 2^20 ns), as it is for an attribute. Samples are the weighted count per step
  (ADR-0122), zero where a bucket had no spans, so every series covers the range and a heatmap has no holes. The
  instant query gives the count over the whole range per bucket.
- **At most 20 groups** (the busiest by span count), against 200 series for the other functions, because each
  group is up to 63 series. A larger result carries `status: PARTIAL` and a message. The row cap of the query is
  raised to fit (`MaxHistogramRows`); an overflow is an error, never a silent cut.

### Exemplars

- **Asked for by `exemplars=n` on the request (what Grafana sends) or `with(exemplars=true)` in the query
  (100 exemplars).** At most 1,000. Range queries only: an instant query has no time axis to put them on.
- **One extra ClickHouse query, after the aggregate:** for each step and group, the span with the largest value
  (`argMax(TraceId, v)`, `max(v)`), the `n` largest overall. The filter, top-group restriction and service scope are
  the aggregate's, so they cannot disagree. It is skipped when the aggregate found nothing. The cost is one more
  read of the matching spans in the window.
- **The span with the largest value is the exemplar,** not a random one as in Tempo: for a latency chart that is
  the trace worth opening. Its value is the duration in seconds, or the attribute's number; its timestamp is the
  span's start.
- **Labels: `trace:id` and `span:id`.**
- **Which series carries it:** the group's series for `rate`, `count`, `avg`, `min`, `max` and `sum`; for
  `quantile_over_time`, the largest quantile's (a slow trace belongs on p99); for a histogram, the bucket the
  exemplar's value falls in (the next larger one if that has none in the window).
- **Sampled spans** (ADR-0122) are not weighted: an exemplar is a real trace, whatever the weight stands for.

## Alternatives considered

- **Several exemplars per step.** Tempo keeps two per bucket. One per step and group already links every point;
  more can be added by raising the `LIMIT` and the grouping without changing the response.
- **Pick an exemplar per histogram bucket.** It multiplies the groups by up to 63; the largest-value span per
  step lands in one bucket and shows the tail, which is what the heatmap is for.
- **Return exemplars from the aggregate query.** `argMax` per group and step fits, but it would make every query
  carry it; most do not ask.

## Consequences

- A Grafana TraceQL query of `histogram_over_time(duration)` renders as a heatmap; with exemplars on, the points
  link to the traces.
- Exemplars follow whatever the series builder keeps: a `topk` (ADR-0224) drops an exemplar with the step it
  removed.
- Not covered: exemplars on the instant query, and a histogram of more than 20 groups.
