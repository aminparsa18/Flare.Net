# ADR-0220: Attributes a query compared on, echoed on `spanSets` spans

Status: Accepted

Date: 2026-10-14

Amends [ADR-0219](0219-tempo-search-span-sets.md), whose consequences left the compared attributes out.

## Decision

- **Each span of a `spanSets` entry lists, besides `service.name`, the attributes the query compared on,** as
  Tempo does: a `{ span.http.status_code >= 500 }` result shows `http.status_code` on the span, so Grafana's nested
  span table gains the column that says why the row is there. Keys are the bare attribute name, without
  `span.` or `resource.`, as in Tempo.
- **Only the named keys are read,** not the bag. `TraceMatchedSpansQueryBuilder.EchoedKeys` collects the
  `(bag, key)` pairs from the plan's conditions, and the matched-spans query selects
  `mapFilter((k, v) -> k IN {keys}, SpanAttributes)` (and the same for `ResourceAttributes`). A bag no
  condition names is a typed empty map and the column is not read. At most 20 keys in all
  (`MaxEchoedAttributes`), so a query that names many attributes cannot widen every row without limit.
  Scope attributes are not echoed: TraceQL has no scope for them.
- **Old and new semantic-convention spellings are all requested.** A condition on `http.status_code` matches a span
  that has `http.response.status_code` (`SemconvAliases`, as the filter itself does), so every spelling is read
  and a span lists whichever it has. A client that asked for `http.status_code` may see
  `http.response.status_code` back.
- **Values are strings.** Flare stores attribute values as strings, so each is a `stringValue`, where Tempo
  would write `intValue` for a number. Grafana shows both the same.
- **A key present in both bags is listed once,** the span's value winning, and `service.name` is never repeated
  (it is written first, from the span's service). Empty values are dropped: a map reports an absent key as empty
  and the two are indistinguishable.

## Consequences

- No new query: the columns ride the existing matched-spans query, which already reads the page's traces
  by `TraceId`. For a query that compares on attributes it reads the two attribute maps of the spans of at most
  200 traces; one that does not (`{ name = "x" }`) reads neither.
- `Absent` and `!=` conditions echo a key the span usually does not have, so nothing is listed for it.
- Grafana's results table gets a column per attribute across all listed spans; with 20 keys that is a wide
  table, which is the query author's choice.
