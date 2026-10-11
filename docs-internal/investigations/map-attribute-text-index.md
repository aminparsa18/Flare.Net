# Investigation: `keyValuePairs` text index for attribute filters

Date: 2026-10-10
Related: [ADR-0184](../adr/0184-defer-keyvaluepairs-attribute-index.md), `db/clickhouse/0001_logs.sql`
(`idx_log_attr_*`, `idx_res_attr_*`), `src/Flare.Api/Query/LogFilterSqlBuilder.cs` (`AttributeClause`).

## Problem statement

`LogAttributes` / `ResourceAttributes` filters are served by `mapKeys` / `mapValues` bloom filters, which cannot tie a
value to its key. ClickHouse 26.9 adds a `keyValuePairs` text-index tokenizer that answers `map['key'] = 'value'` from the
index. Is it worth adding?

## Setup

ClickHouse 26.9.14.10 (throwaway container). 5,000,000 rows over one hour, 6 services, Flare's `ORDER BY`. Three tables with
identical data and a narrowed column set (Timestamp, TraceId, SeverityNumber, ServiceName, Body, both attribute maps):
no attribute index, today's four bloom filters, and `INDEX ... TYPE text(tokenizer = keyValuePairs)` on each map.
Attributes: `http.method` (4 values), `http.status_code` (7), `tenant.id` (500), `user.id` (100k), `http.route` (60),
resource `k8s.pod.name` (400) and others, plus `feature.flag` / `order.id` on 1 row in 1000 (so every granule holds the key).
Each query was run with `use_query_condition_cache = 0`; `read_rows` and duration come from `system.query_log`.
The feature is not behind an experimental setting on 26.9, and builds on the real `Map(LowCardinality(String), String)`.
The data is synthetic; absolute sizes will differ on real logs.

## Findings

### 1. Equality (`m[k] = v`): same or better pruning, much faster when pruning is impossible

| Predicate | matches | no index | bloom (today) | text index |
|---|---:|---|---|---|
| `order.id = 'ord-3000'` | 1 | 5.0M rows, 218 ms | 57K rows (7 granules), 16 ms | 8K rows (1 granule), 12 ms |
| `user.id = '4242'` | 50 | 5.0M, 176 ms | 459K (56 gran.), 41 ms | 410K (50 gran.), 17 ms |
| `feature.flag = 'nope'` (absent) | 0 | 5.0M, 200 ms | 74K, 17 ms | 0 rows, 7 ms |
| `tenant.id = 't17'` | 9.8K | 5.0M, 213 ms | 5.0M, 216 ms | 5.0M, **53 ms** |
| `http.status_code = '500'` | 714K | 5.0M, 191 ms | 5.0M, 195 ms | 5.0M, **55 ms** |
| `pod = 'pod-7' AND method = 'POST'` | 3K | 5.0M, 384 ms | 5.0M, 424 ms | 5.0M, **61 ms** |

Where the bloom filter prunes nothing (low-cardinality values, multi-key ANDs) the text index still cuts latency 4 to 7x:
it filters rows from the index without reading the map column, even with 612/612 granules selected. Result counts matched
in every case. `SELECT count()` alone is answered from the index (2 rows read), which flatters the numbers, so the
table above uses queries that also read `Body`.

### 2. Only equality is served; the bloom filters cannot be dropped

Same corpus, text index with the bloom filters removed:

| Predicate | bloom (today) | text index only |
|---|---|---|
| `mapContains(m, k)` / `NOT mapContains` | 188 / 245 ms | 284 / 366 ms (scan) |
| `mapContains AND m[k] IN (2 values)` | 90K rows, 20 ms | 5.0M rows, 235 ms |
| `m[k] IN ('4242','77','9999')` | 1.25M rows, 72 ms | 5.0M rows, 349 ms |
| `NOT (mapContains AND m[k] = 'GET')` | 5.0M, 241 ms | 5.0M, 322 ms |
| `mapContains AND match(m[k], ...)` | 193 ms | 247 ms |

`IN` is not rewritable to `OR`: ClickHouse folds an equality chain of 3 or more back into `IN`, which the index does not
serve. A two-value `OR` was pruned (2 granules). So the text index would be added next to the bloom filters, not
replace them.

### 3. Cost

| | bloom (4 indexes) | text index (2 indexes) |
|---|---:|---:|
| Index size, 5M rows | 6.2 MiB | **96.7 MiB** (`LogAttributes` 66.5, `ResourceAttributes` 30.2) |
| Table data (all columns here) | 220 MiB | 220 MiB |
| Insert of 1M rows | 4.0 s | 3.4 s (no measurable penalty) |

About 15x the bloom filters' size, 44% of the compressed table in this corpus. The `user.id` high-cardinality key is
the main driver, and real attribute cardinality is unknown. Merge cost and a real corpus were not measured.

### 4. Not available on the pinned LTS

`create table ... INDEX i m TYPE text(tokenizer = keyValuePairs)` on 26.8.2.7 (the version
[ADR-0183](../adr/0183-pin-clickhouse-image.md) pins) fails with `Unknown tokenizer: 'keyValuePairs'`. A numbered
migration using it fails on every supported install.

## Not evaluated

Body search on a `text` index (26.9 lets `LIKE`/`ILIKE` use it). It is a separate question from the attribute index and
needs its own benchmark against `ngrambf_v1` (ADR-0073), including the 26.9 backslash change noted in ADR-0183.
