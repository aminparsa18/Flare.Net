# ADR-0226: Grafana Traces Drilldown against the Tempo API: root spans, `nil`, hints, and `compare()`

Status: Accepted

Date: 2026-10-16

Extends [ADR-0225](0225-traceql-metrics-comparison-and-span-relations.md), which left `compare()` on the roadmap behind
Traces Drilldown compatibility because Drilldown, its only consumer, failed on its first request.

## Context

Grafana's Traces Drilldown app (`grafana-exploretraces-app`, installed into Grafana 13.2.4 with a Tempo data source at
`http://host.docker.internal:8099/tempo`, the API running locally against ClickHouse with two seeded services) was driven
tab by tab, and every request it made to Flare was read from the API's request log, not guessed. The queries are Tempo's
own TraceQL with these constructs:

- `nestedSetParent<0` selects root spans, and `&& true` is appended so that filters have something to follow.
- `resource.service.name != nil` and `span.http.route != nil` select spans that have an attribute.
- `| rate()  with(sample=true)` and `| histogram_over_time(duration) with(sample=true)`; `by(x)` without a space.
- `{...} | compare({status = error}, 10)` as a range query with `step=3600s`, in the Comparison tab.

## Decision

- **`nestedSetParent` is a test for "is a root".** It is -1 for a root span and the parent's positive position in the
  trace otherwise, so a comparison with a whole number is read as which of the two kinds of span it lets through: `< 0`,
  `<= -1`, `= -1` are root spans; `> 0`, `>= 1`, `!= -1`, `> -1`, `= 3` are spans with a parent; a test both satisfy (`< 100`) restricts
  nothing; one neither satisfies (`< -1`, or `> 0 && < 0`) is an error naming the test, not an empty result.
  Implemented as `TraceSpanCondition.RootSpan` (`bool?`, appended last so older MemoryPack clients are unaffected),
  compiled to `ParentSpanId = ''` / `!= ''` by `ConditionMatch`, so a search, a metric and a structural query agree. The other nested-set
  intrinsics (`nestedSetLeft`, `nestedSetRight`) stay unsupported.
- **A bare `true` is a predicate that changes nothing** (`{ nestedSetParent<0 && true }`); a bare `false` is an error.
- **`x != nil` is "the attribute exists", `x = nil` "it does not"** (`Exists` and `Absent`, which attribute filters
  already had); other operators with `nil` are errors. `nil` was rejected before.
- **`with(...)` takes a comma-separated list of `exemplars` and `sample`.** `sample` (a boolean or a number) is read and
  ignored: it asks Tempo for an approximate answer, and Flare's is exact. Any other hint is still an error naming it.
  A hint after a later stage no longer resets an earlier `exemplars=true`.
- **`compare({ selection }[, topN[, start, end]])`.** Tempo's grammar, read from its source and docs: the spans of the
  query's spanset that match the selection are the selection, the rest the baseline; for every attribute, the `topN`
  (default 10, at most 1,000) most common values of each; optional unix-nanosecond `start` and `end`, both or neither,
  narrow the selection to spans that started after the first and at or before the second. No `by`, and no `topk`,
  `bottomk` or comparison after it (Tempo says the same). The query's spanset can be anything a metrics query takes,
  including a relation and root-span fields.
  - **Output** (Tempo's labels, checked against Grafana, which draws it): for each attribute and value
    `{ __meta_type="selection", span.http.route="/a" }` and `baseline`, whose samples are the spans per step (weighted
    by `SampleWeight`, ADR-0122; zero in a step without any); `{ __meta_type="selection_total", span.http.route=nil }`
    and `baseline_total` per attribute, counting every span that has it whatever the value, so a share can be computed;
    and, when either group has more values than `topN`, one `{ __meta_error="__too_many_values__", span.x=nil }` series
    without samples. A `nil` label value is written as an empty value (`"value": {}`).
  - **Attributes:** `name`, `status` (`error`, `ok`, `unset`), `kind` (`server`, ...), every `span.x` and every
    `resource.x` the spans carry. Values are strings, as Flare stores them. An empty value, and one longer than 256
    characters (a URL, a stack trace: not a category), is not counted.
  - **Two passes in one query** (`TraceQlMetricsQueryBuilder.BuildCompare`): the first ranks `(group, attribute, value)` by span count and keeps
    `topN + 1` per group and attribute (the extra one shows the caller there are more), the second counts per step with every
    other value read as empty, so the rows still add up to the attribute's total per step. The `IN` is evaluated in a `SELECT`
    of its own because ClickHouse 26.8's analyzer loses the column when it sits inside the grouped expression.
  - **Limits:** at most 200 steps and 500,000 result rows (an error, not a silent cut), and `max_rows_to_group_by` of 20 million with `throw`,
    since the query expands every attribute of every span. On the 47,000-span test table a 30-day query took 0.23 s.
  - **Steps are the request's:** Drilldown sends `step=3600s` over a one-hour range, which aligns to two steps when the range
    crosses an hour; each series has a sample per step, and with two steps Drilldown's percentages and "highest difference" came out right, so the steps are not a problem for it.
  - **Not done:** `compare()` over several spansets, exemplars, and typed values (an integer attribute is a string label).

## Verified in Grafana

With the API on the seeded data, Traces Drilldown's tabs were loaded and screenshotted:

| Tab | Result |
|---|---|
| Overview panels (span rate, error rate, duration heatmap), Breakdown by service or attribute, Errors breakdown, Duration breakdown | Work. |
| Traces | Lists the 200 root spans. |
| Comparison | Works: ranks `span.http.status_code` first (all baseline spans 200, all selection spans 500), then `status`, then `name` and `span.http.route`. |
| Service structure, Root cause errors | Fail: they send a union structural search, `({...} &>> { kind = server }) \|\| ({...}) \| select(status, resource.service.name, name, nestedSetParent, nestedSetLeft, nestedSetRight)`, which needs the `&>>` operator, `select()` and nested-set numbering of the spans. |
| Exceptions (count 0) | The search `\| select(resource.service.name, event.exception.message, ...) with(most_recent=true)` is rejected; the tab shows 0. Needs `select()` and the `event.` scope. |

A request Drilldown makes with an attribute named `undefined` (`{... && undefined != nil}`) is rejected, as Tempo rejects it too: it is not a field.

## Not done

- **Service structure and Root cause errors:** `&>>` (a union structural operator returning both sides), `select()` and the nested-set
  positions of a trace's spans (a numbering of its span tree, computed per trace).
- **Exceptions:** `select()` and span events as an attribute scope, `with(most_recent=true)`.
- **`&&` and `||` between spansets in a metrics query**, which no Drilldown query uses.

## Consequences

- Grafana's Traces Drilldown opens against Flare and its overview, Breakdown, Comparison and Traces views work; three tabs
  that need span-tree queries stay empty or show an error.
- `{ nestedSetParent < 0 }` is a cheap way to say "root spans" in a plain Tempo data source query too.
