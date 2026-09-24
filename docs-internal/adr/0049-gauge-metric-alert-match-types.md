# ADR-0049: Last / Min / Max match types for Gauge metric alerts

Status: Accepted

Date: 2026-09-24

## Context

A Gauge `MetricThreshold` rule always compared the window's average
(`MetricAlertAggregation.Value` = `avg(Value)`). Two common rules couldn't be
written precisely:

- "Disk free below 5% right now." The average lags: a disk that just filled up
  still has a healthy 10-minute average.
- "CPU over 90% for the whole 10 minutes." One spike can pull the average over
  90%, and one dip can pull it under.

Prior art: SigNoz's match types (at least once / all the time / on average / in
total / last), with "last" added in
[signoz#5929](https://github.com/SigNoz/signoz/commit/4edc6dbeae834162a64ddce2f575f4d61244f913).

## Decision

**Three new `MetricAlertAggregation` members, Gauge only: `Last`, `Min`, `Max`.**
They are appended to the enum, so stored rules and MemoryPack ordinals don't
change. No migration is needed: the aggregation lives inside
`MetricConditionJson`. This is the same additive shape as ADR-0020 and ADR-0044.

- `Min` = `min(Value)` and `Max` = `max(Value)` over the window.
- `Last` = each series' latest point (`argMax(Value, Time)`, grouped by
  `ServiceName` + `toString(DataPointAttributes)`, the same series identity
  ADR-0044 partitions by), then averaged across series. With one matched series,
  this is just the latest point. Taking the latest point across *all* series
  would instead report whichever host happened to report last.

**No separate "all the time" / "at least once" members.** With the two
comparators Flare has, both reduce to `Min` or `Max`:

| Comparator | All the time | At least once |
|------------|--------------|---------------|
| `>=` T     | `Min >= T`   | `Max >= T`    |
| `<` T      | `Max < T`    | `Min < T`     |

Direction-aware members would need `EvaluateMetricConditionAsync` to know the
comparator. That method is also the value source for anomaly rules (ADR-0048),
which have no comparator. Keeping the enum comparator-free lets anomaly rules
use `Last`/`Min`/`Max` unchanged. The alert form bridges the gap with a hint
under the aggregation picker that relabels `Min`/`Max` for the chosen comparator.

**An empty window stays NaN.** ClickHouse's `min()`/`max()` over zero rows
return `0`, not NaN. That would fire a "free space `<` 5%" rule on silence.
`Min`/`Max` therefore use `if(count() = 0, nan, …)`. `Last` gets NaN for free:
zero inner rows make the outer `avg()` NaN. Absent-data alerting (ADR-0045)
still owns "no data".

## Consequences

- Sum and Histogram rules are unaffected. The form only offers the new members
  for Gauge. A hand-crafted Sum/Histogram request carrying them falls back to
  that type's existing behavior (the Sum `Value` read / NaN for Histogram).
- `Last` is a `GROUP BY` subquery, not a flat aggregate. It reads the same rows
  as `avg(Value)`, under the same `EvaluationSafetyOptions` caps.
