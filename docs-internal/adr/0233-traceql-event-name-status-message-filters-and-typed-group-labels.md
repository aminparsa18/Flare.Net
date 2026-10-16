# ADR-0233: TraceQL `event:name` and `statusMessage` as filters, and typed `by (span.x)` labels

Status: Accepted

Date: 2026-10-16

Extends [ADR-0229](0229-traceql-kind-sets-and-colon-intrinsics.md), [ADR-0230](0230-traceql-select-trace-fields-all-events-typed-values.md) and
[ADR-0228](0228-traceql-value-groups-and-scoped-intrinsics.md). First part of a second fixed list of five gaps left by the Tempo API follow-ups
(`event:name`, `statusMessage`, typed group labels, a combination of spansets as the selection of `compare()`, and the parent and ancestor
structural operators). This part is the three small ones.

## Decision

### `event:name` is the name of an event, not an event attribute

ADR-0228's tokenizer read `event:name` as `event.name`, the *attribute* called `name` on an event, which no event has: the filter matched nothing.
An event's name is the `Events.Name` array of the span row. `event:name` is now an `Intrinsic` filter (ADR-0229) on that column, with the same
treatment as the link ids: **true when some event of the span satisfies the test**.

- `event:name = "exception"`, `!=`, `=~`, `!~` (anchored, as everywhere in this API), a `||` group of `=` (an `IN`), `= nil` and `!= nil`.
- `!=` is "some event whose name differs", as `link:spanID !=` is "some link that differs". `= nil` is "the span has no event"; `!= nil` "has one".
- It is available wherever a condition is: search, the structure builder, every metrics path and `compare()`'s selection, because they all
  compile a condition through `SpanFilterSqlBuilder.AttributeClause`.
- `event.name` (the attribute, scoped with a dot) is **left alone**: it still reads the event attribute map.

### `select(event:name)` is not built

Tempo lists it as an attribute of each event keyed `event:name`. Flare would have to read a second array for the selector and invent a key in the
event attribute lists of ADR-0230. No client we have seen sends it (Traces Drilldown's Exceptions view selects `event.exception.*`), and the
event names are already reachable as a filter. It is rejected with the usual `select(...) is not supported` message, which lists what is selectable.

### `statusMessage` and `span:statusMessage` as a filter

`StatusMessage` is a plain string column, so the filter is one more entry of the intrinsic column list and the planner case that threw `not supported`
now maps to it. `=`, `!=`, `=~`, `!~`, a `||` group of `=`, `= nil` (an empty message: every span without one) and `!= nil` (has a message). An
ordering (`>`) is an error naming the operators it takes. `select(statusMessage)` already worked (ADR-0230). `childCount`, `parent`,
`nestedSetLeft` and `nestedSetRight` stay `the intrinsic '...' is not supported` as filters.

### `by (span.x)`, `by (resource.x)` and `by (.x)` labels are typed

The series labels of a metrics query's `by (...)` fields built in `TempoMetricsSeries.Build` now follow the rule `compare()` already uses
(ADR-0230): a value that `TempoTypedValue.Infer` reads as a whole number, a decimal or a bool is an `intValue`, `doubleValue` or `boolValue`; anything
else, `0123` included, stays a `stringValue`.

- `by (name)`, `by (status)` and `by (kind)` stay text: `name = 200` is a name.
- **Grouping and ordering are untouched.** The groups are still the stored strings, joined as before, ranked by weight and then by that text, so
  `"1000"` still sorts before `"500"` on a tie. The label is derived from the group's text afterwards, and two different texts never give the
  same label (the rule only accepts a canonical form), so no two series collapse.
- **Exemplar labels are unaffected**: `trace:id` and `span:id` are written as text. An exemplar is attached to its series by the group's
  text, not by the label.
- A span's missing attribute is still left out of a `by`, not labelled with an empty value.

## Verified

Against ClickHouse with 444 seeded spans (60 traces from `tqseed-front` and `tqseed-back`; exception events 1 to 3 on the `log` span of each trace and a
`retry` event on every third; status messages `boom-N` and `logfail-N`; attributes that are ints, doubles, bools and `0123`/`42`/`abc`/`007`). Every
API number is compared with a hand-written SQL that does not use the builder's `arrayExists`: `ARRAY JOIN` over `Events.Name` with `count(DISTINCT TraceId, SpanId)`,
`length(Events.Name)`, or a plain `WHERE` on the column.

| Query (`\| count_over_time()`) | API | SQL |
|---|---|---|
| `event:name = "exception"` / `= "retry"` | 60 / 20 | 60 / 20 |
| `event:name != "exception"` | 20 | 20 |
| `event:name =~ "ret.*"` / `!~ "exc.*"` | 20 / 20 | 20 / 20 |
| `event:name = nil` / `!= nil` | 384 / 60 | 384 / 60 |
| `(event:name = "retry" \|\| event:name = "exception")` | 60 | 60 |
| `event.name = "exception"` (the attribute) | 0 | 0 |
| `statusMessage = "boom-4"`, `!= "boom-4"` | 1, 443 | 1, 443 |
| `span:statusMessage =~ "boom.*"` / `statusMessage !~ "boom.*"` | 15 / 429 | 15 / 429 |
| `statusMessage = nil` / `!= nil` | 419 / 25 | 419 / 25 |
| `(statusMessage = "boom-4" \|\| statusMessage = "logfail-6")` | 2 | 2 |

A search for `event:name = "retry"` listed the same 20 traces and the same spans as the `JOIN` over the span rows, and `statusMessage =~ "logfail.*"` the same
10 traces and spans. `by (span.http.status_code)` gave `intValue` 200, 500 and 503 at 40 spans each, equal to `GROUP BY`; `by (span.tq.code)` gave `intValue` 42 and
`stringValue` `007`, `0123`, `abc` at 45 each; `by (span.tq.flag)` gave `boolValue` 30 and 30; `by (span.tq.ratio)` gave `doubleValue` 0.5, 0.75 and `intValue` 1
(an OTLP `1.0` is stored as `1`); `by (resource.tq.region)` `stringValue` `eu`; `by (span.http.status_code, name, status, kind)` typed only the first.

## Consequences

- `{ event:name = "exception" }` and `{ statusMessage =~ "timeout.*" }`, as the Tempo documentation writes them, run and match.
- A client that reads the label of `by (span.http.status_code)` gets a number it can sort. A client that matched the old string label compares
  against `intValue` now, as it already does for `compare()`.
- A string that only looks like a number (an order number) is an int in a label: the cost of storing text (ADR-0230).
