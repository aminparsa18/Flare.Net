# ADR-0184: Defer the `keyValuePairs` text index for attribute filters

Status: Accepted

Date: 2026-10-10

## Context

The roadmap carried an item to benchmark ClickHouse 26.9's `keyValuePairs` text-index tokenizer against the `mapKeys` /
`mapValues` bloom filters that serve `LogAttributes` / `ResourceAttributes` filters. The benchmark is in
[`../investigations/map-attribute-text-index.md`](../investigations/map-attribute-text-index.md):

- It serves `m[k] = v` only, and prunes as well as or better than the bloom filters (1 vs 7 granules on a rare value).
  When no pruning is possible it still cuts latency 4 to 7x (about 55 ms vs 200 to 420 ms on 5M rows).
- It does not serve `mapContains`, `IN`, regex or `NOT` forms, which regress to full scans without the bloom filters. It
  would be added beside them, costing about 15x their size (96.7 vs 6.2 MiB per 5M rows, 44% of table data here).
- It does not exist on 26.8, the LTS line pinned by [ADR-0183](0183-pin-clickhouse-image.md). A migration using it fails
  there.

## Decision

**Do not add the index now. Revisit when the pinned line moves to 26.9 or later.**

- No migration, no `LogFilterSqlBuilder` change. Attribute equality keeps using the bloom filters.
- When the pin is bumped past 26.9, re-run the investigation on a real corpus (attribute cardinality, merge cost, index
  size) before deciding. A likely shape is an index on `LogAttributes` only (69% of the index size here), applied via a
  migration that is a no-op on servers without the tokenizer.

## Consequences

- No storage growth and no migration risk on 26.8 installs.
- Broad equality filters (`http.status_code = 500`, multi-key ANDs) stay at a full-window scan.
- The roadmap keeps a short item for the revisit, tied to the version bump.
