# Investigation: do attribute filters use the `mapValues` bloom indexes?

Date: 2026-10-04
Related: former roadmap item "Investigate: do attribute filters use the
`mapValues` bloom indexes?" (removed with this write-up); prior art
[signoz PR #12614](https://github.com/SigNoz/signoz/pull/12614)

## Problem statement

The migrations define `bloom_filter` indexes on `mapValues(LogAttributes)`,
`ResourceAttributes` and `SpanAttributes`, but `AttributeClause` emits
`map[key] = v` (plus a `mapContains` guard for some operators), never
`has(mapValues(map), v)`. The worry was that ClickHouse could not connect the
two forms, leaving the value indexes as dead write amplification.

## Method

ClickHouse 26.8.2.7 (`clickhouse/clickhouse-server:latest`, throwaway
container). Table is the `logs` shape trimmed to what matters: same
`ORDER BY`/partitioning and the same two `LogAttributes` indexes
(`mapKeys` and `mapValues`, `bloom_filter(0.01)`, `GRANULARITY 1`).
20M rows over one day, 5 services, 2443 granules. `LogAttributes` has
`user.id` (200k distinct values, random per row), `tenant` (50 values),
`http.method` (3) and `rare.flag` (`yes` on 0.01% of rows). Each query was
checked with `EXPLAIN indexes = 1`.

## Findings

**The indexes are used, with the SQL as it is today.**

| Predicate (as `AttributeClause` emits it) | `idx_log_attr_value` granules |
|---|---|
| `map['user.id'] = '12345'` (Equals) | 136 / 2442 |
| `mapContains(..) AND map['user.id'] = '12345'` | 136 / 2442 |
| `mapContains(..) AND map['user.id'] IN (3 values)` | 352 / 2442 |
| the same three plus a redundant `has(mapValues(..), v)`, `hasAny`, an OR-chain of `has`, or `arrayExists` | identical to the row without it |

Wall time (4 threads, 3 runs each) with and without skip indexes:

| Query | indexes on | indexes off |
|---|---|---|
| Equals, one `user.id` | ~0.07 s | ~0.75 s |
| `IN` with 3 `user.id` values | ~0.15 s | ~0.71 s |

So the redundant `has(mapValues(..))` predicate proposed in the roadmap item
changes nothing: the planner already derives the bloom lookup from the
map-subscript comparison. `idx_log_attr_value` is 23.6 MiB for 20M rows.

**What the index cannot do** (inherent, not fixable by rewriting SQL):

- `Exists`/`Absent` can only use the key index, and it prunes nothing when
  every row carries the key.
- A value that appears in most granules (`tenant = '1'`, `http.method = 'GET'`)
  skips nothing, whatever the predicate shape. The value index pays off for
  high-cardinality values (ids) and rare values.
- The value index is bag-wide: it indexes all values, not `(key, value)`
  pairs, so a value that also occurs under another key (a `user.id` of `1`
  against `tenant = 1`) is a false positive for the bloom.
- `NotEquals`/`NotIn` (negated) and `Regex` cannot use a bloom lookup.

## Pitfall hit while measuring

A first pass used `'1'`, `'2'`, `'3'` as `IN` values and showed 2442/2442 for
every variant. Those strings are also `tenant` values, so every granule
legitimately contained them. Test values must be unique to the key under
test, or the experiment reports "index not used" when it is only unselective.

## Conclusion

No change to `LogFilterSqlBuilder`, `SpanFilterSqlBuilder` or the migrations.
Resource and span attribute bags use the same index type and the same
subscript predicate form, but were not benchmarked separately here.
