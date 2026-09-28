# Investigation: does the Logs free-text search use `idx_body`?

Date: 2026-09-28
Related: `db/clickhouse/0001_logs.sql` (`idx_body`),
`src/Flare.Api/Query/LogFilterSqlBuilder.cs` (`Search` → `Body ILIKE {search:String}`),
[`benchmark-ingest-and-query.md`](benchmark-ingest-and-query.md) pattern (e),
`docs-internal/planning/roadmap.md` (the "Research: does the Logs free-text
search actually use `idx_body`?" item this answers).

## Problem statement

`LogFilter.Search` compiles to `Body ILIKE '%…%'`. The only index on `Body` is
`idx_body Body TYPE tokenbf_v1(32768, 3, 0)`. ClickHouse doesn't document
bloom-filter skip indexes as usable for `ILIKE`, so every free-text search might
be scanning `Body` for the whole time window.

## What was checked

Throwaway ClickHouse 26.8.2.7 container (`clickhouse/clickhouse-server:latest`,
the same image `docker-compose.yml` uses) with `db/clickhouse/*.sql` mounted
as init scripts, so the `logs` table and its indexes are exactly what ships.
Seeded 5,000,000 rows over one hour, then ran `OPTIMIZE TABLE logs FINAL`
(one part, 613 granules). Nearly all bodies were
`user N fetched item M in Xms status ok`. One row held the rare token
`ZqxKafkaFailure`, and 5 rows held `upstream Connection Refused by peer …`.

For each predicate: `EXPLAIN indexes = 1`, then the query itself with
`use_query_condition_cache = 0`, reading `read_rows` from `system.query_log`.

## Findings

### 1. `ILIKE` never consults `idx_body`, so every search is a full scan

| Predicate | `idx_body` in plan | Granules kept | Rows read |
|---|---|---:|---:|
| `Body ILIKE '%zqxkafkafailure%'` (what Flare sends) | **not listed** | - | 5,000,000 |
| `Body ILIKE '%connection refused%'` | not listed | - | 5,000,000 |
| `hasTokenCaseInsensitive(Body, 'zqxkafkafailure')` | not listed | - | 5,000,000 |
| `lower(Body) LIKE '%zqxkafkafailure%'` | not listed (expression mismatch) | - | 5,000,000 |
| `Body LIKE '%ZqxKafkaFailure%'` | listed | 613/613 | 5,000,000 |
| `Body LIKE '%Connection Refused%'` | listed | 613/613 | 5,000,000 |
| `Body LIKE '% ZqxKafkaFailure %'` | listed | **3/613** | 24,576 |
| `hasToken(Body, 'ZqxKafkaFailure')` | listed | **3/613** | 24,576 |

Only the ORDER BY key and the time window limit what a search reads. `idx_body`
has never helped a dashboard search. It costs about 17.7 MiB here, against
33.8 MiB of compressed `Body`, and its only real use is `hasToken` or a `LIKE`
with the token padded by non-alphanumeric characters. Flare generates neither.

So pattern (e) in the ingest/query benchmark (271 ms p50) measured a scan
bounded by time and ORDER BY, not the index.

`tokenbf_v1` can't help with `LIKE '%x%'` even when case matches. The pattern's
edge tokens may be partial words, so the index can only use tokens bounded on
both sides. With a single word between `%` wildcards, no token qualifies.

### 2. An `ngrambf_v1` index on the lowered body works, including partial words

After `ALTER TABLE logs ADD INDEX idx_body_ngram lower(Body) TYPE ngrambf_v1(4, 60000, 5, 0) GRANULARITY 1`
and `MATERIALIZE INDEX` (these are SigNoz's parameters, from signoz#4787):

| Predicate | Granules kept | Rows read |
|---|---:|---:|
| `lower(Body) LIKE '%zqxkafkafailure%'` | 1/613 | 8,192 |
| `lower(Body) LIKE '%kafkafail%'` (partial word) | 1/613 | 8,192 |
| `lower(Body) LIKE '%connection refused%'` (phrase) | 5/613 | 40,960 |
| `lower(Body) LIKE '%zqx\_kafka%'` (escaped `_`) | 0/613 | - |
| `lower(Body) LIKE '%fetched item%'` (in every row) | 613/613 | 5,000,000 |
| `lower(Body) LIKE '%zqx%'` (3 chars, shorter than n=4) | 613/613 | 5,000,000 |

That's a drop from 5M rows read to 8K for a selective term. Searches matching
most rows, and search strings shorter than the ngram size, still scan, which is
correct. `lower(Body) LIKE lower('%…%')`, with `lower()` applied to the
parameter, is also matched to the index. On this synthetic data the index was
32.1 MiB, about the size of compressed `Body`. The data has high-cardinality
numbers in every row, so that's a pessimistic figure. Real templated log lines
should compress better, but check index size on real data before settling on
`60000` bytes per granule.

For comparison, ClickHouse's newer `text` (inverted) index on `lower(Body)`
with `tokenizer = splitByNonAlpha` came out at 62.8 MiB and didn't prune the
phrase search (613/613). It doesn't suit substring semantics, so it was
dropped.

### 3. `lower()` would silently break non-ASCII searches. Use `lowerUTF8()`

The SigNoz-style rewrite uses `lower()`, which folds ASCII only. `ILIKE` folds
UTF-8, so the rewrite changes results:

```
SELECT lower('İSTANBUL ÄÖÜ'), lowerUTF8('İSTANBUL ÄÖÜ'), 'ÄÖÜ' ILIKE 'äöü'
-- İstanbul ÄÖÜ    i̇stanbul äöü    1
```

On a 1M-row table with one row `'Ошибка ПОДКЛЮЧЕНИЯ к базе'` and
`idx_body_ngram` built on `lowerUTF8(Body)` instead:

| Predicate | Matches | Granules kept |
|---|---:|---:|
| `Body ILIKE '%ошибка подключения%'` (today) | 1 | not indexed |
| `lower(Body) LIKE '%ошибка подключения%'` | **0** (regression) | - |
| `lowerUTF8(Body) LIKE lowerUTF8('%ошибка подключения%')` | 1 | 1/123 |
| `lowerUTF8(Body) LIKE '%подключения%'` | 1 | 1/123 |

The dashboard ships ru and zh-CN, so Cyrillic log bodies are expected. The
index expression and the query must both use `lowerUTF8`. `ngrambf_v1` works
on bytes, so a 4-gram covers about 2 Cyrillic characters or about 1.3 CJK
characters. Pruning still worked in the test above, but the minimum useful
length of a CJK search string is shorter than it looks.

## What this implies for the fix

Implemented in migration 0036 and
[ADR-0073](../adr/0073-logs-body-ngram-search-index.md). The ADR also has a
filter-size comparison, which settled on `ngrambf_v1(4, 32768, 3, 0)`.

- New additive migration: `ADD INDEX idx_body_ngram lowerUTF8(Body) TYPE ngrambf_v1(4, …, 5, 0) GRANULARITY 1`,
  mirrored in `db/clickhouse-cluster/`. Keep `idx_body`: dropping it isn't
  additive, and it still serves `hasToken`.
  `MATERIALIZE INDEX` is optional. Without it, only parts written after the
  migration are pruned, and older parts are scanned as they are today.
- `LogFilterSqlBuilder`: `lowerUTF8(Body) LIKE lowerUTF8({search:String})`.
  `ContainsPattern`'s `%`/`_`/`\` escaping carries over unchanged, since the
  `LIKE` escape rules are the same. Existing `LogFilterSqlBuilder` tests that
  assert the `ILIKE` SQL text need updating.
- A second `Body ILIKE` site exists: `LogQlWhereTranslator.TranslateComparison`
  compiles LogQL `Body LIKE '…'` to `Body ILIKE`, which has the same
  full-scan problem. It should get the same `lowerUTF8(Body) LIKE lowerUTF8(…)`
  rewrite when the left-hand side is the `Body` column. The `json(Body, …)`
  and attribute-values `ILIKE` sites run on extracted values, so no `Body`
  index could serve them, and they stay as they are.
- Alert rules reuse `LogFilter`, so the builder change also covers them. The
  live-tail `LogFilterMatcher` uses
  `Contains(…, StringComparison.OrdinalIgnoreCase)`, which is Unicode-aware.
  That matches `lowerUTF8` parity better than `lower` would.
