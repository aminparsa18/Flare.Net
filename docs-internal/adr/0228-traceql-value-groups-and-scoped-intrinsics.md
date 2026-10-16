# ADR-0228: TraceQL value groups `(f = a || f = b)` and Tempo's scoped intrinsics

Status: Accepted

Date: 2026-10-16

Extends [ADR-0217](0217-tempo-compatible-trace-api.md) and [ADR-0227](0227-traceql-select-union-descendant-and-nested-sets.md).
The rule for this change: build a TraceQL construct only if a real client sends it and it fails today. The remaining items
of the roadmap's "Tempo API follow-ups" were each checked against that rule; two clients turned out to send queries the
items did not name, and those are what this ADR builds.

## What was checked, and what was found

The Tempo data source plugin bundled with Grafana 13.2.4 ships its source maps, so the queries it builds were read from its
TypeScript and then driven in Explore with the API's request log open.

- **Search tab, several values picked.** The builder writes
  `(resource.service.name="a" || resource.service.name="b") && (name!="x" && name!="y") && (status=error || status=ok)`
  (`filterToQuerySection`: a multi-value `=` is a parenthesised `||` group, a multi-value `!=` a `&&` group). Flare answered
  400 `unexpected '('` and Grafana showed "failed to execute search query" on the most ordinary interaction of the tab.
  The same query shapes (`{ name != "a" && name != "b" }`, `{ status != ok && status != unset }`) failed as "compared
  twice" when typed by hand.
- **Tempo's scoped intrinsics.** Tempo's docs and cookbook write `{ trace:rootService = "gateway" }`, `{ trace:duration > 2s }`
  and `{ span:duration > 100ms }`, and the TraceQL editor autocompletes them. Flare answered 400 `'trace:rootService' is not a field`.
- **Not sent by any client, so left on the roadmap:** typed attribute values (Grafana's result transformer accepts a string
  where Tempo would send an int, so a table column is text but nothing breaks), `select()` of trace-level fields (the Search
  tab and Drilldown never generate it), the sibling and negated structural operators, `&&` / `||` between spansets in a
  metrics query, and event attributes beyond a span's first event. Explore's Search and TraceQL tabs, the Drilldown views
  (ADR-0227) and the docs' examples were all run for this; none of those sends them.

## Decision

- **A spanset's body is a small boolean expression.** Terms joined by `||`, items joined by `&&`, parentheses anywhere. A
  parenthesised `&&` group is its predicates written out. A `||` group is accepted only when every branch is an `=` on the
  same field (a bare `true` branch makes it hold for every span); it becomes one predicate carrying the other values
  (`TraceQlPredicate.Alternatives`, at most 200). `{ name = "a" || span.x = "b" }` and `(a && b) || c` stay errors, naming the
  rule: a span condition is a conjunction, and an OR of conjunctions would need the spansets-level `||`, which does not
  mean the same thing next to `&&` or a relation.
- **Planner.** Name, status and kind keep a set of the allowed values, narrowed by each `=` (intersection, or the group's
  values) and `!=` (removal), and resolved when the condition is built:
  - name: one value is `Name = x` (or `!=`); several are one anchored RE2 alternation of the escaped values (`Regex` /
    `NotRegex`), so no model change;
  - status: one label, or the label that is left out, negated, when two of the three remain (`error || ok` is `!= unset`);
  - kind: one value, or all but one; a set of two to four kinds is an error (no client sends it; Flare does not advertise
    `kind` in the intrinsic scope, so the builder never offers it);
  - a contradiction (`name = "a" && name = "b"`) is `no span ... satisfies`, which is more accurate than "compared twice";
    mixing a pattern (`=~`) with exact tests on `name` is still "compared twice".
  - an attribute group is a `SpanAttributeFilterOperator.In` filter (existing operator and SQL, promoted columns included);
  - `rootName` / `rootServiceName` groups join the trace-level lists, as repeated `=` already did;
  - `resource.service.name` groups use a new `TraceSpanCondition.ServiceNames` (appended last, MemoryPack
    wire-compatible): `ServiceName IN (...)` in `ConditionMatch`, and the structure builder's service prefilter takes the
    union of the conditions' services, so a multi-service search keeps its primary-key prefilter.
  - `duration`, `nestedSetParent` and the other intrinsics reject a group, naming the fields that take one.
- **Scoped intrinsics are read at tokenization** as the unscoped names (`span:duration` is `duration`, `trace:rootService` is
  `rootServiceName`, `event:name` is `event.name`, ...), so filters, `select()`, `by()` and metrics values all take them.
  The ones with no counterpart (`span:parentID`, `event:timeSinceStart`, `instrumentation:*`, `link:*`, `span:id`,
  `trace:id`, `span:statusMessage`) say `the intrinsic '...' is not supported`.
- The metrics queries and `compare()` go through the same planner, so `{ (name = "a" || name = "b") } | rate()` works too.

## Verified

Against ClickHouse on the 47,000-span test table, span counts through `count_over_time()` equalled `count()` for: services
`IN`, a name `IN`, a name `NOT IN` within one service, `status = error || ok` against `!= unset`, and a three-way
combination (157 = 157). Grafana's Search tab with two Service Names, two excluded Span Names and two statuses now loads the
Traces table; Drilldown's Breakdown, Comparison, Service structure and Exceptions tabs still load, and the only non-200
response is the app's own `undefined` attribute query, which Tempo rejects too.

## Not done

- Sets of two to four span kinds; `||` between anything but values of one field; `(a && b) || c`.
- Everything under "not sent by any client" above.

## Consequences

- Grafana's Search tab works with any number of values per field, and queries written from Tempo's docs run unchanged.
- A name group costs a regex over `Name` rather than an equality; a service group a `ServiceName IN`.
