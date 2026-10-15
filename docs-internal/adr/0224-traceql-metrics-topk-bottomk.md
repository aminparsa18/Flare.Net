# ADR-0224: `topk()` and `bottomk()` after a TraceQL metrics function; what stays out

Status: Accepted

Date: 2026-10-15

Extends [ADR-0221](0221-traceql-metrics.md), which rejected every second pipeline stage.

## Decision

- **`| topk(n)` and `| bottomk(n)` after the metrics function (and its `by`).** `{ ... } | rate() by (name) | topk(5)`.
  `n` is a whole number of 1 or more. `with(exemplars=...)` may follow it. A comparison after it
  (`| topk(5) > 10`) and any other stage are rejected, naming the stage.
- **Evaluated at each step, as in Prometheus and Tempo.** Of the series with a sample at a step, the `n` with the
  largest (smallest) value keep it; the others lose that sample. Ties go to the earlier series, which is the one with
  more spans. A series left with no sample is dropped, so a result can hold more than `n` series overall
  (a series that is first in one step and not in the next). The ranking runs over the series Flare computed,
  quantile and histogram-bucket series included, like Tempo.
- **In the API, after the ClickHouse query,** on at most 200 series by their steps: it needs no SQL change, and the
  ranking per step is easier to get right in memory than as a window function over `Bucket`.
- **Limit:** the query keeps the 200 groups with the most spans (ADR-0221) before the ranking. A group outside
  those but with the largest average cannot be returned by `topk` of an average; the `PARTIAL` status already says
  groups were cut.
- **Exemplars** (ADR-0223) stay only with a step their series kept.

## Not done, and why

- **`compare({ selection })`.** It returns, for every attribute a span carries, the distribution of values in the
  selection against the rest: a scan that unrolls the attribute maps of every span (an `ARRAY JOIN` over three maps),
  counts each key and value for two span sets, ranks them and returns them in a response with its own label
  scheme (`__meta_type`), whose rendering in Grafana is a different chart. That is a query type of its own, not a
  function in the aggregate, and a dashboard panel rarely uses it. Left on the roadmap.
- **Structural spansets in a metrics query (`{ a } >> { b } | rate()`).** Which spans does it count, and when?
  The right-hand spans that have a matching ancestor, evaluated per trace before the per-step aggregate. That is a
  per-trace pass (ADR-0069) joined back to the spans, on the whole window, and a different cost model from the
  single scan the aggregate is. ADR-0221 rejected it for the same reason; nothing changed.
- **Trace-level intrinsics (`rootName`, `traceDuration`)** in the spanset, for the same per-trace reason.
- **A comparison after the aggregate (`| rate() > 5`).** Small, but it needs a filter stage the series builder
  does not have; it can follow without changing this.

## Consequences

- `{ ... } | rate() by (span.http.route) | topk(5)` works in a Grafana panel, and so does `| bottomk(3)`.
