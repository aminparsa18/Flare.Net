# ADR-0073: Index-backed Logs free-text search

Status: Accepted

Date: 2026-09-28

## Context

`LogFilter.Search` compiled to `Body ILIKE '%term%'`. The only index on
`Body` is 0001's `idx_body`, a `tokenbf_v1`. Evidence gathered on
ClickHouse 26.8 with 5M rows is in
[`../investigations/logs-body-search-skip-index.md`](../investigations/logs-body-search-skip-index.md):

- `ILIKE` never consults any skip index. Every search read every row in
  its time window, 5M of 5M, including a search for a word in one row.
- `tokenbf_v1` can't serve `LIKE '%term%'` even when the case matches,
  because the edges of the pattern may be partial words. It only helps
  `hasToken` and token-padded patterns, and Flare generates neither.
- An `ngrambf_v1` index on the lowercased body cut the same rare-word
  search to one granule (8K rows), and also pruned partial words and
  phrases.

The same `ILIKE` reaches ClickHouse from LogQL `Body LIKE '…'`, and from
log-count alert rules, which reuse `LogFilterSqlBuilder`.

## Decision

### `lowerUTF8(Body) LIKE lowerUTF8(pattern)` over an ngram index on `lowerUTF8(Body)`

Migration 0036 adds
`idx_body_ngram lowerUTF8(Body) TYPE ngrambf_v1(4, 32768, 3, 0) GRANULARITY 1`
(on `logs_local` in the cluster variant). `LogFilterSqlBuilder.BodyLikeSql`
emits `lowerUTF8(Body) LIKE lowerUTF8({param:String})` for the Search
filter and for LogQL `Body LIKE` / `NOT LIKE`. A skip index is only used
when the query expression matches the index expression exactly, so
`LogFilterSqlBuilder.BodyIndexExpr` holds that expression, and a unit test
checks both migration files against it.

`lowerUTF8`, not `lower` as in SigNoz's version of this change
([signoz#4787](https://github.com/SigNoz/signoz/commit/1585065fff9b7853d63e64abebf2887ecc42cc72)).
`lower` folds ASCII only. A search for `ошибка подключения` against
`Ошибка ПОДКЛЮЧЕНИЯ` matched with `ILIKE` and with `lowerUTF8`, but
returned 0 rows with `lower`. The dashboard ships ru and zh-CN. `LIKE`
escaping is the same as `ILIKE`'s, so `ContainsPattern` is unchanged, and
the live-tail `LogFilterMatcher` (`OrdinalIgnoreCase`) was already
Unicode-aware.

### Sizing: 32 KB per granule, 3 hashes

Measured on 5M templated log lines (HTTP, SQL, order, cache and job
messages with ids, UUIDs and durations). Compressed `Body` was 55 MiB:

| Filter | Index size | Rare word | Partial word | Phrase | Numeric id |
|---|---:|---:|---:|---:|---:|
| `(4, 8192, 3, 0)` | 3.4 MiB | 1/611 | 193/611 | 50/611 | 597/611 |
| `(4, 16384, 3, 0)` | 9.6 MiB | 1/611 | 1/611 | 5/611 | 550/611 |
| `(4, 32768, 3, 0)` (chosen) | 19.2 MiB | 1/611 | 1/611 | 5/611 | 522/611 |
| `(4, 60000, 5, 0)` (SigNoz) | 35.1 MiB | 1/611 | 1/611 | 5/611 | 495/611 |

The cells are granules kept. Anything from 16 KB up prunes word and phrase
searches equally. 32 KB is the same budget `idx_body` already uses. It
leaves headroom for real log lines, which are longer than these test lines
and so fill a granule's filter faster. It costs half of SigNoz's size.
Digit-heavy searches barely prune at any size, because digit ngrams
appear in most granules.

### Additive, not materialized

`idx_body` stays: dropping it isn't additive (CLAUDE.md), and it still
serves `hasToken`. The migration doesn't `MATERIALIZE INDEX`, because that
would rewrite index data across the whole `logs` table on the next startup
of every install. New parts are indexed on insert and merged parts as they
merge. Unindexed old parts are scanned exactly as before, and searches
default to a 1-hour lookback. The migration comment gives the
`MATERIALIZE INDEX` command for anyone who wants history indexed at once.

## Alternatives considered

- **`hasToken` / token search on the existing index.** Case-sensitive and
  whole-word only. A search box that stops finding `timeout` inside
  `ReadTimeoutException` is a behavior regression, not an optimization.
- **ClickHouse's `text` (inverted) index.** On `lowerUTF8(Body)` with
  `splitByNonAlpha`, it was 1.9x the size of the 60 KB ngram filter on the
  first test set, and didn't prune the phrase search (613/613). It fits
  token queries, not substring queries.
- **`MATERIALIZE INDEX` in the migration.** Rejected above: it's a
  table-wide mutation for a benefit limited to data older than the default
  search window.

## Consequences

- Selective searches read a few granules instead of the whole window. That
  applies to the Logs explorer, the CLI's `search`/`export`, LogQL, and
  log-count alert evaluation.
- Search strings shorter than 4 bytes, and terms present in most granules,
  still scan, which is correct and no slower than before.
- `logs` storage grows by roughly a third of `Body`'s compressed size.
- The index and query expressions are coupled. Changing either without the
  other quietly brings full scans back, which is why the unit test exists.
