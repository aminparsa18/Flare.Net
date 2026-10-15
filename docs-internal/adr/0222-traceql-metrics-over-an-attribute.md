# ADR-0222: TraceQL metrics over a numeric attribute

Status: Accepted

Date: 2026-10-15

Extends [ADR-0221](0221-traceql-metrics.md), which accepted `avg_over_time`, `min_over_time`, `max_over_time`,
`sum_over_time` and `quantile_over_time` of `duration` only, because a function over an attribute needs a numeric
cast of a string map value and a decision about spans where it does not parse.

## Decision

- **Syntax: the argument is `duration`, `span.x`, `resource.x` or `.x`.** `avg_over_time(span.size)`,
  `quantile_over_time(resource.k8s.pod.cpu, .5, .99)`, `sum_over_time(.http.response.body.size) by (name)`. `.x`
  makes the same span-or-resource guess a predicate makes. The other intrinsics (`name`, `status`, `kind`) are not
  numbers and are rejected naming the function and the field. `rate()` and `count_over_time()` take no argument.
- **A string value is cast with `toFloat64OrNull`,** the cast the numeric attribute operators already use
  (`NumericAttributeComparison`), so a metric and a `{ span.size > 100 }` predicate agree on what is a number.
- **A span whose value is absent, does not parse or is not finite is left out,** the way `by` leaves out a span
  without the grouped attribute (ADR-0221): it takes no part in the sums, the quantiles or the ranking of the top
  series. It is not read as zero, which would pull an average down for spans that simply never reported the
  attribute. `NaN` and the infinities, which `toFloat64OrNull` accepts, are left out as well, as one of them would
  turn the whole step's sum into `NaN`.
- **Weights apply as for duration.** Sums, averages and quantiles weigh each value by `SampleWeight` (ADR-0122);
  `min` and `max` do not. The weight of a row counts only the spans that had a number.
- **Units: a duration is divided by 1e9 to seconds, an attribute is reported as it is.** The unit of an attribute
  is whatever it was recorded in; Flare has no way to know.
- **Semantic-convention spellings** read whichever the span has, as `by` does (`SemconvAliases`). If the first
  spelling present holds text that is not a number the span is left out; the second spelling is not tried.
- **Not done:** a promoted attribute column (ADR-0062, ADR-0063) is not used for the value, as `by` does not use
  one; the map read is correct, a column would only be faster.

## Alternatives considered

- **Count unparseable values as zero.** Rejected, as above: it makes the average depend on how many spans never
  carried the attribute.
- **Reject the query when the attribute has non-numeric values.** Rejected: it would need a second read to find out,
  and real attributes mix numbers and `"unknown"`.

## Consequences

- `avg_over_time(span.http.response.body.size)`, `quantile_over_time(span.queue.depth, .9)` and the like work in a
  Grafana TraceQL panel against Flare.
- A typo in an attribute name gives an empty result, not an error, as for `by`.
