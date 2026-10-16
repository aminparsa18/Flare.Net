# ADR-0227: TraceQL `select()`, the union descendant `&>>`, nested-set positions and event attributes in search

Status: Accepted

Date: 2026-10-16

Extends [ADR-0226](0226-traces-drilldown-compatibility-and-compare.md), which left three Traces Drilldown tabs failing:
Service structure and Root cause errors (`&>>`, `select()`, nested-set positions) and Exceptions (`select()` over span
events, `with(most_recent=true)`).

## Context

The Drilldown app's own code was read (the plugin bundle in the Grafana container), not guessed, to see what it needs of
the response, then every tab was driven against Flare with the API's request log open.

- **Service structure, Root cause errors and Root cause latency are one scene** with one query, parameterized by the metric
  type: `({<root> [&& status = error | duration > x]} &>> { kind = server | status = error | true }) || ({<root> ...})
  | select(status, resource.service.name, name, nestedSetParent, nestedSetLeft, nestedSetRight)` with `spss=20`. It reads
  the *raw* search response and throws unless every trace has **exactly one span set**, whose spans carry integer
  `nestedSetLeft` and `nestedSetRight` attributes (`intValue`, a string, as in protobuf JSON), `service.name` and `status`
  string attributes, and the span's `name`. It sorts the spans by left number and builds the tree by containment (a span is
  a child when its left number is above and its right number below another's).
- **Exceptions** sends `{<root> && status = error} | select(resource.service.name, event.exception.message,
  event.exception.stacktrace, event.exception.type) with(most_recent=true)` with `limit=400`, and expects the event
  attributes as `exception.message` and so on (the table accepts the name with or without `event.`). Opening an
  exception sends a filter on the same fields: `{... && event.exception.message = "..." && event.exception.type = "..."}`.
- The latency tab also writes `duration >= 0` (a plain number) and `{ true }` as the descendant side of `&>>`, and the
  duration Comparison tab sends `compare({})`.

## Decision

### Parser and planner

- **`| select(field, ...)`** after the query in a search (not a metrics query), with an optional `with(most_recent=true)`.
  Fields: `name`, `status`, `kind`, `duration`, `nestedSetParent`, `nestedSetLeft`, `nestedSetRight`, `span.x`, `resource.x`, `.x`
  (resolved like a filter's: a resource attribute in a resource namespace, else a span attribute) and `event.x`; at most 20.
  Anything else (`statusMessage`, `rootName`, ...) is an error naming it, and so is any other hint or pipeline stage.
  `most_recent` is read and ignored: a Flare search already lists the newest traces first.
- **`&>>`** (union descendant) between two spansets. Whether a trace has such a pair is the question `>>` answers, so the
  trace-level expression is `A => B`; what differs is which spans are listed (below). The parser keeps the pair
  (`TraceQlUnionRelation`). `&>>` in a metrics query is rejected, naming the operator: a metrics query counts one side.
- **`event.x` is a filter scope** (`SpanAttributeBag.Event`, appended last to the MemoryPack enum): the span matches when
  some event of it does. Compiled to `arrayExists(m -> <the ordinary clause over m>, Events.Attributes)`, so every
  operator works; `= nil` is `NOT arrayExists(... mapContains ...)`, "no event has the key". Never a promoted column.
- **An empty spanset next to `>`, `>>` or `&>>` is "any span"**, compiled as a condition `DurationNano >= 0` because a structure
  condition must say something. Combined with `&&` or `||` it is still an error (it would change nothing, or everything).
  This also applies to a relation in a metrics query (`{ name = "a" } >> { } | rate()`), which ADR-0225 rejected.
- **A duration compared with a plain number is in nanoseconds** (`duration >= 0`), as in Tempo, where a duration and a number
  are comparable.
- **`compare({})` selects every span** (the baseline is empty); ADR-0226 rejected it.

### Which spans a search lists, and what each carries

A search with `select()` or `&>>` no longer asks the matched-spans query (ADR-0219) for "the first few spans matching any
condition". `TraceSpanSelectQueryBuilder` reads, for the traces the search returned (at most 200), their spans with a
**bit per structure condition** (`ConditionMatch`, so a search, a metric and this agree on what matches), the span's
ids, name, service, start, duration, kind and status, and only the requested keys of the span, resource and event
attribute maps. It reads **every span of each trace** when the numbering or a `&>>` needs the tree (a union relation, or a
`nestedSet*` field), and otherwise only the spans matching some condition. At most 2,000 spans per trace, the earliest by
start; the rest are numbered as if absent, so a span that loses its parent this way becomes a root. The decision is
made in memory by `TempoSpanSelector`, a pure class:

- **Which spans.** A span is listed if it satisfies a condition that is not on either side of a `&>>`, or takes part in one:
  a right-hand span with a matching ancestor, or a left-hand span with a matching descendant, ancestors followed up to 64
  levels as in the structure builder (ADR-0069). For Drilldown's query that is the root span (it is also the plain `||`
  branch) and the server spans below it, which is exactly what the app draws.
- **Nested sets**, Tempo's numbering computed per trace in memory: depth-first from each root, children in start-time
  order, `nestedSetLeft` the next number on entering a span, `nestedSetRight` the next on leaving it, `nestedSetParent`
  the parent's left number or -1. A span whose parent is not among the rows is numbered as a root after the true roots
  (so "root" for `nestedSetParent < 0` in SQL, which is `ParentSpanId = ''`, and in the numbering can differ for an
  orphan); a parent cycle cannot loop, and a 50,000-deep chain does not overflow the stack (iterative walk).
  Computed in the API, not SQL, because the numbers are positions in the whole trace and only the returned traces
  (at most 200) need them. A SQL version (not tried) would be a per-trace walk over every span in an `arrayFold`, as the
  `=>` evaluation is, with no gain for a result this small.
- **Order and `spss`.** Spans are listed in walk order when they are numbered (the tree reads best, and truncating keeps its
  top), else start-time order; `matched` counts every listed-eligible span, the response carries the first `spss`.
- **Attributes**, keyed without their scope as Tempo returns them (`resource.service.name` is `service.name`,
  `event.exception.type` is `exception.type`): `service.name` first, then the selected fields in the order written
  (`status` as `error`/`ok`/`unset`, `kind` as `server`..., the nested-set numbers as integers), then the attributes the
  query compared on. The first of a key wins. An attribute the span lacks is left out. Event attributes come from the first
  event that has any of the selected event keys, so `message`, `type` and `stacktrace` stay together.
- **One span set per trace**, written as before in both `spanSets` and the deprecated `spanSet`.

### Cost

On the test table (a 30-day window, the 200-trace limit, Drilldown's structure query) the search took 0.3 s: the trace
discovery as before, plus one read of the spans of 200 traces by `TraceId`, the first column of the sort key. The read
runs with `max_result_rows` lifted to `200 x 2000` and `result_overflow_mode=throw`.

## Verified in Grafana

Grafana 13.2.4 with Traces Drilldown, the API on a local ClickHouse, three seeded services (40 traces: frontend, cart,
payment, database spans, errors with exception events on the root and on a child span). Every tab was loaded for each of
the three metric types (rate, errors, duration) and the API log read for non-200 responses.

| Tab | Result |
|---|---|
| Service structure, Root cause errors, Root cause latency | Work: a tree per root service with its server spans (frontend > payment > charge), the span counts in the panel title. |
| Exceptions | Works: the exceptions of the root spans grouped by type and message with their count; opening one sends the `event.exception.*` filter and it loads. |
| Breakdown, Comparison (including the duration view's `compare({})` and `duration >= 0`), Traces | Still work. |

The only non-200 responses are the app's queries with an attribute named `undefined`, which Tempo rejects too.

## Not done

- **`&&` and `||` between spansets in a metrics query**, which no Drilldown query uses (unchanged from ADR-0225).
- **The other structural operators** (`&>`, `&~`, `!>>`, `~`, ...), which no Drilldown query uses.
- **`select()` of `rootName`, `rootServiceName`, `traceDuration`, `statusMessage`, `childCount`, `parent`**, and aggregates in
  a search pipeline (`count()`, `avg()`, `by()`).
- **Event attributes in more than the first event** of a span: a span with several exception events lists the first one's.
- **Typed attribute values.** Flare stores attribute values as strings, so `select(span.http.status_code)` returns a string.

## Consequences

- Every tab of Grafana's Traces Drilldown loads against Flare without a query error.
- A search with `select()` or `&>>` reads all the spans of the returned traces when it asks for the tree: bounded by 200 traces
  and 2,000 spans each, and by the query caps every query carries.
- `{ } >> { ... }` and `{ ... } >> { }` are now valid in a metrics query as well as a search (any ancestor or descendant).
