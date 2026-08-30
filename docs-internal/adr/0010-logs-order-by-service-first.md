# ADR-0010: `logs` sorts by `(ServiceName, SeverityNumber, Timestamp, TraceId)`, not time-first

Status: Accepted
Date: 2026-08-07 (v1, `0001_logs.sql`)

## Context

`logs`' `ORDER BY` (ClickHouse's physical sort/primary key) has to pick one
column ordering, and that ordering determines which query shapes are cheap
(hit the sorted prefix) versus expensive (unsorted scan within a
partition). Two access patterns the dashboard actually needs genuinely
conflict: "browse one service's logs over a time range, optionally
narrowed by severity" (the common case — a dev viewing their own service)
versus "browse everything, no service filter" (an all-services live-tail
default view).

## Decision

`ORDER BY (ServiceName, SeverityNumber, Timestamp, TraceId)` — low-to-high
cardinality, leading with the two columns the dashboard filters by most.

## Alternatives considered

- **`Timestamp`-first ordering**, favoring the all-services live-tail view
  and any time-range-only query over the service-scoped case. Not chosen:
  the service-scoped browsing pattern was judged the more common real
  usage to optimize for. Recorded explicitly as the named follow-up if
  this trade-off stops holding: a `Timestamp`-first *projection*
  (ClickHouse's mechanism for maintaining a second physical sort order of
  the same data) is addable non-destructively later, without revisiting
  this ADR's primary `ORDER BY`.

## Consequences

- **Known, deliberately unresolved trade-off, stated plainly rather than
  hidden**: an unfiltered, no-service query cannot use the `ORDER BY`
  prefix and falls back to a broader scan within whatever partitions the
  time range prunes to. `Flare.Api`'s `/api/logs/search` requires/defaults
  a bounded time range specifically so an unfiltered query is at least
  pruned to a handful of monthly partitions — but a query with no
  `Services` filter still can't benefit from the sort order itself.
- Skip indices (`bloom_filter` on `TraceId` and the attribute-map
  keys/values, `tokenbf_v1` on `Body`) exist specifically to cover the
  filters that fall *outside* this `ORDER BY` prefix — trace/span
  correlation, structured-attribute filtering, and body substring search
  — rather than trying to fold every access pattern into the sort order
  itself.
- **Benchmarked, not just theorized**: the cheap end of this design (a
  service-scoped query, or a skip-indexed exact match) measured 3-10x
  cheaper than an unscoped scan at 5M rows — see
  [the benchmark investigation](../investigations/benchmark-ingest-and-query.md).
  That same investigation also surfaced a genuine surprise worth
  cross-referencing here: at 5M rows, the *unscoped attribute/body-search*
  patterns (which also miss the `ORDER BY` prefix) were slower than the
  unscoped aggregate query — sharpening exactly which off-prefix pattern
  is the real cost driver in practice, not just in theory.
- The named `Timestamp`-first-projection follow-up remains deferred as of
  this writing, pending further real query-latency evidence — see the
  benchmark investigation's own "Unresolved / follow-ups."

## Related documentation

- `db/clickhouse/README.md` — `0001_logs.sql` and the skip-index detail
- `docs-internal/investigations/benchmark-ingest-and-query.md` — the
  latency evidence this decision's trade-off was measured against
- ADR-0011 — the equivalent, differently-resolved trade-off for `spans`