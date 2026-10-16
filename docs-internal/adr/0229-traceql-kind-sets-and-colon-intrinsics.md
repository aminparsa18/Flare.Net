# ADR-0229: TraceQL sets of span kinds and the colon intrinsics Flare has data for

Status: Accepted

Date: 2026-10-16

Extends [ADR-0228](0228-traceql-value-groups-and-scoped-intrinsics.md), which rejected both. ADR-0228 built only what a
client was seen sending; the remaining items of the Tempo follow-ups are now built from a fixed list, whether or not a
client sends them. This is the first part: span-kind sets and the intrinsics that need a column of the `spans` table
rather than a name Flare already knew.

## Decision

### A set of two to four span kinds

- **`{ kind = server || kind = client }`, and the same written with `!=`** (`kind != internal && kind != unspecified`), is
  a membership test on the `Kind` column. The planner narrows a set of allowed kinds with each `=` (intersection, or the
  group's values) and `!=` (removal), as it does for name and status (ADR-0228), and resolves it when the condition is built:
  one kind is `Kind = x`; five of six is `Kind != x`; none of six is no test; two to four is the new case.
- **`TraceSpanCondition.Kinds`** (`IReadOnlyList<byte>?`, appended last: MemoryPack wire-compatible, as `ServiceNames`
  was in ADR-0228). `ConditionMatch` compiles it to `Kind IN {tsXKinds:Array(UInt8)}`. It is ANDed with `Kind` when both
  are set (the planner never sets both), counts as "something to match on" in the structure builder's validation, and
  each number is checked against the OTel range. Every consumer of a condition (search, structure, metrics, `compare()`,
  matched spans) gets it from `ConditionMatch`.
- A contradiction (`(kind = server || kind = client) && kind != client && kind != server`) is still `no span satisfies`.

### Colon intrinsics: what Flare stores, one-to-one

`SpanAttributeBag.Intrinsic` (appended last) is a filter on a column or per-element value of the span rather than on an
attribute map; `SpanAttributeFilter.Key` names it, from a fixed list (any other key is an error, so the bag is not a way
to read an arbitrary column). It reuses the attribute filter's operators, so a condition, the structure builder and every
metrics path take it unchanged.

| TraceQL | Flare | Notes |
|---|---|---|
| `span:id` | `SpanId` | |
| `span:parentID` | `ParentSpanId` | a root span's is empty: `span:parentID = nil` is "is a root", `!= nil` "has a parent" |
| `trace:id` | `TraceId` | |
| `instrumentation:name` | `ScopeName` | the OTel instrumentation scope's name, as written |
| `instrumentation:version` | `ScopeVersion` | |
| `link:spanID`, `link:traceID` | `Links.SpanId`, `Links.TraceId` | true when *some* link satisfies the test; `= nil` / `!= nil` is "no link" / "has a link" |
| `event:timeSinceStart` | each event's `Events.TimeUnixNano` minus the span's `StartTime` | true when some event satisfies the comparison |

- **Text operators.** The id, scope and link columns take `=`, `!=`, `=~`, `!~` (anchored, as everywhere in this API), a
  `||` group of `=` (an `IN`), and `= nil` / `!= nil` (empty and non-empty; for a link, any and none). `>` and the other
  orderings are an error naming the field.
- **Ids are lower-cased and left-padded with zeros** (to 16 characters for a span or parent id, 32 for a trace id) when
  the written value is hexadecimal and not longer than a full id, which is how Tempo reads a short id; `"AB"` is
  `00000000000000ab`. A value that is not hexadecimal, or is too long, is compared as written (so it matches nothing)
  and a pattern is never touched.
- **`event:timeSinceStart` is a duration** compared with `=`, `!=`, `>`, `>=`, `<`, `<=` (a plain number is nanoseconds,
  as for `duration`). Each test is made against the span's events on its own, the way two `event.x` tests are (ADR-0227):
  `> 1ms && < 5ms` is satisfied by one event above 1 ms and another below 5 ms, not necessarily the same one. Holding
  both tests to a single event would need a per-event condition, which a span condition does not have.
- The old reading of `span:id` and `trace:id` as the unscoped `spanID` / `traceID` (neither exists in TraceQL) is gone.

### Still not supported, with the reason in the error

`span:statusMessage` and `statusMessage` as a *filter* (`select()` of it is the next change), `childCount`, `parent`, `nestedSetLeft` and
`nestedSetRight` as filters: `the intrinsic '...' is not supported`. `select()`, `by()` and metrics values take none of
the colon intrinsics of this ADR; they are filters.

## Verified

Against ClickHouse on the 47,000-span test table plus 345 seeded spans (60 traces from two services, three scopes, links, and
exception events at known offsets), the span count of `count_over_time()` equalled the hand-written `count()`:

| Query | API | SQL |
|---|---|---|
| `kind = server \|\| kind = client` | 13,798 | `Kind IN (2,3)` 13,798 |
| `kind = producer \|\| kind = consumer \|\| kind = internal` | 33,648 | `Kind IN (1,4,5)` 33,648 |
| three `!=` leaving 2, 3, 4 | 22,906 | `Kind IN (2,3,4)` 22,906 |
| `span:id`, `span:parentID`, `trace:id` of one span | 1, 1, 5 | 1, 1, 5 |
| `instrumentation:name = "tq.back"`, `instrumentation:version != "2.1.0"` | 160, 185 | 160, 185 |
| `span:parentID = nil` (roots of the seeded services) | 60 | `ParentSpanId = ''` 60 |
| `link:spanID = <id>`, `link:traceID = <id>`, `link:spanID != nil` | 1, 1, 27 | `has(Links.SpanId, ...)` 1, 1, 27 |
| `event:timeSinceStart > 25ms`, `<= 20ms` (front service) | 22, 34 | `arrayExists` over the offsets 22, 34 |

A search for `kind = server || kind = client` over a three-hour window listed the same 5 traces as the equivalent SQL.

## Consequences

- Queries written from Tempo's docs that use `span:id`, `trace:id`, `link:*`, `instrumentation:*` and
  `event:timeSinceStart` run instead of failing with 400.
- An id test (`trace:id = ...`) is a scan of `TraceId`, the first column of the sort key, so it is cheap; a pattern on an id or a scope is a
  scan of the window like any other pattern.
- `SpanAttributeBag.Intrinsic` is reachable from the Flare span APIs that accept a `SpanFilter`, with the same fixed key list.
- Old clients ignore the new members: neither `Kinds` nor the new enum value is written by them.
