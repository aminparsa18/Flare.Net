# ADR-0230: `select()` of `statusMessage` and the trace fields, the attributes of every event, and typed attribute values

Status: Accepted

Date: 2026-10-16

Extends [ADR-0227](0227-traceql-select-union-descendant-and-nested-sets.md), which listed three things as not done: `select()` of
`statusMessage`, `rootName` and friends; event attributes beyond a span's first event; typed attribute values. Second part of the
fixed list started in [ADR-0229](0229-traceql-kind-sets-and-colon-intrinsics.md).

## What Tempo does (read from its source, `pkg/traceql/engine.go`, not guessed)

- A span's attributes in a search result are its `AllAttributes()`, minus `name`, `duration`, `traceDuration`, `rootServiceName`,
  `rootName`, the trace id and the span id. So `select(rootName)`, `select(rootServiceName)` and `select(traceDuration)` add **no
  span attribute**: those are the trace's own fields in the result, `rootTraceName`, `rootServiceName` and `durationMs`.
  `statusMessage` is an ordinary string attribute.
- An attribute is typed by what the source stored: `intValue`, `doubleValue`, `boolValue`; a duration and a status or kind are
  `stringValue`s.
- Attributes are a map, so a key is there once, and with several events the **last** event's value overwrites the others
  (and a key one event lacks keeps an earlier event's value, mixing events).

## Decision

### `select()` of `statusMessage`, `rootName`, `rootServiceName`, `traceDuration`

- All four are accepted, also as `span:statusMessage`, `trace:rootName`, `trace:rootService`, `trace:duration` (ADR-0228).
- **`rootName`, `rootServiceName`, `traceDuration` need nothing added to the spans**: Flare's search result row already carries the
  trace's root name, root service and duration (`rootTraceName`, `rootServiceName`, `durationMs`), which is what Tempo's own result
  does for them. They are read so a query copied from Tempo runs.
- **`statusMessage` is a `stringValue` attribute** on each listed span that has one. The span rows read the column only when the
  query selects it (`SelectedSpansRequest.StatusMessage`, `SpanTreeRow.StatusMessage`).
- `statusMessage` as a *filter* is still `the intrinsic ... is not supported`: only `select()` is in scope.

### The attributes of every event

- A span's `event.x` attributes are listed for **each event of the span**, in the span's event order, each event's selected
  attributes together in the order the fields were written. For `select(event.exception.type, event.exception.message,
  event.exception.stacktrace)` a span with two exception events lists `type, message, stacktrace` of the first, then the same of the
  second. An event lacking a key leaves it out; an event with none of the selected keys lists nothing.
- **A key therefore appears once per event.** A client that takes the first of a key (Traces Drilldown's Exceptions tab) sees the first
  event, as before; one that keeps the last (Grafana's table) sees the last event whole, which is Tempo's behaviour without its mixing.
- At most **50 events** of a span are listed (`TempoSpanSelector.MaxEventsPerSpan`), so a span with thousands of events cannot
  inflate a response. The events are still read from ClickHouse as before (only the selected keys of each).
- The event fields are written where the first of them appears in the `select()`; the later ones are part of the same groups.
  Attributes outside events keep the "first of a key wins" rule.

### Typed attribute values

Flare stores every attribute value as a string (OTLP ints, doubles and bools were written as their text at ingest: `200`, `0.536`,
`true`), so the type is gone and has to be inferred. The rule, decided once (`TempoTypedValue.Infer`): **a value is a number or a
bool only when writing that number or bool back gives exactly the stored text.**

| Stored text | Result |
|---|---|
| `true`, `false` (exactly) | `boolValue` |
| a 64-bit whole number in canonical form: `200`, `-3`, `0` | `intValue` (a string, as protobuf JSON writes an int64) |
| a finite number with a `.` or `E` that .NET's shortest round-trip format (`"R"`) prints back unchanged: `0.25`, `-1.5`, `1E+21` | `doubleValue` |
| anything else: `0123`, `007`, `+5`, `-0`, `5.0`, `1.50`, `1e5`, ` 5`, `1_000`, `NaN`, `True`, a number beyond 64 bits, ids | `stringValue` |

- **Ids and codes with a leading zero stay strings**, as does `5.0`, which would print back as `5`; so no version, code or id
  loses a character. What the rule cannot do is tell an all-digit string from a count: an order number `1000000001` is an int. That is
  the cost of having no stored type, and it matches what the source sent in the common case (a count, a status code, a size).
- **Where it applies:** the values of span, resource and event attributes in a search result, both in the `select()` path and in the
  attributes a query compared on (one list, one rule), and the label values `compare()` writes for `span.x` and `resource.x`
  attributes. It does **not** apply to intrinsics (`name`, `status`, `kind`, `statusMessage`), to `service.name`, or to the
  nested-set numbers (already integers). A service called `123` is a string.
- `by (span.x)` labels in other metrics queries stay strings (not in this change).

## Verified

Against ClickHouse on the test table plus 345 seeded spans (60 traces; exception events 1 to 3 per span; status codes as ints, a flag as
a bool, a ratio as a double, and a code cycling through `0123`, `42`, `abc`, `007`):

| Check | API | SQL |
|---|---|---|
| `select(statusMessage, rootName, ...)`: spans with a `statusMessage` attribute | 19 | `StatusMessage != ''` on error spans 19 |
| `select(event.exception.*)` on the 34 spans with events: attributes, in order | 207 attributes, 34 of 34 spans equal | the `Events.Attributes` arrays, element by element |
| `select(span.http.status_code, span.tq.flag, span.tq.ratio, span.tq.code)` over 60 root spans | 60 int, 60 bool, 60 double, `tq.code` 14 int / 46 string | 60 / 60 / 60, `tq.code = '42'` 14, other 46 |
| `compare({ status = error })` label `span.http.status_code`, selection total | `intValue` 500: 16, 503: 8, 200: 8 | `GROUP BY` 16, 8, 8 |

## Not done

- `by (span.x)` labels as typed values; `select()` of `childCount` and `parent`.
- Tempo's one-value-per-key behaviour for events (last event wins): the repeated keys above are a deliberate superset.

## Consequences

- A Grafana table column of a numeric attribute is numeric; `span.http.status_code` sorts as a number.
- A column whose values mix numbers and text (a `code` that is sometimes `42`, sometimes `007`) is mixed, as in Tempo for an
  attribute that has more than one type.
- The Exceptions tab of Traces Drilldown still shows the first event's exception per span; a client that wants every event reads
  the repeated keys.
