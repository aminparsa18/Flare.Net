# ADR-0145: Per-resource retention through a computed `_retention_days` column

Status: Accepted

Date: 2026-10-07

## Context

One retention per signal (ADR-0143) is too coarse. Teams want `deployment.environment=dev` kept a week
and everything else a month, or one noisy namespace kept shorter. A ClickHouse TTL is one expression per
table, so the lifetime has to be a property of the row.

## Decision

- **Each raw telemetry table gets `_retention_days UInt16 DEFAULT 20000`** (migration 0069; on the
  `_local` and `Distributed` tables in cluster mode). A TTL then reads the row's own value:
  `toDateTime(Timestamp) + toIntervalDay(_retention_days)`.
- **Rules are ordered `(resource attribute, value, days)` triples; first match wins.** Flare turns them
  into the column's default, `multiIf(ResourceAttributes['k'] = 'v', days, ..., <signal default>)`, with
  `ALTER TABLE ... MODIFY COLUMN _retention_days UInt16 DEFAULT <expr>`. The default is evaluated by
  ClickHouse at insert time, so Ingest is unchanged: its inserts name their columns, and an unnamed
  column takes its default. Cluster mode alters both the `_local` and `Distributed` tables, since the
  Distributed table's default is what fills the column for inserts that go through it.
- **Matching is exact equality on a resource attribute.** Not prefix, regex, or log/span attributes. It
  covers the stated use (environment, namespace, team) and keeps the generated SQL small and checkable.
  At most 20 rules per signal. Attribute keys are restricted to `[A-Za-z0-9_.\-/:]` and values are
  escaped, because both are interpolated into DDL, which can't take parameters.
- **"Keep forever" is `20000`, not 0 and not larger.** A `+ 0 days` TTL deletes the row at once. And
  `DateTime` ends in 2106, so `now + 36500 days` overflows and wraps into the past, which also deletes
  the row (found while testing this: a "forever" rule removed the data it was meant to keep). 20000 days
  is about 55 years and stays in range for any timestamp up to 2051. `RetentionSql.MaxDays` is now
  18250 (50 years), below `ForeverDays` so the two can't be confused. This also fixes ADR-0143's old
  limit of 36500, which would have deleted everything for that input.
- **Order of operations.** Rules on: set the column default, then the TTL. Rules off: point the TTL back
  at a fixed number, then reset the default. The column is never read by a TTL before it computes the
  right value.
- **Cold tiering stays a single fixed age** per signal (ADR-0144) and must be less than the shortest
  finite retention among the rules and the default, or a row could be deleted before it moves.
- **Actual state is read back.** The TTL parser flags the `_retention_days` form; the rules and the
  signal default are parsed out of `system.columns.default_expression` (which ClickHouse re-prints with
  the map access parenthesised). An expression Flare didn't write reads as `custom`. The requested
  rules are kept as JSON in `retention_operations.RulesJson`, so expected and actual can be compared.

## Alternatives considered

- **One table per retention class.** Rejected: queries would need a `UNION`, and moving a service
  between classes would mean rewriting data.
- **`TTL ... DELETE WHERE <rule>` clauses.** ClickHouse allows several, but each rule would add a clause
  to every table's TTL and a rule edit would rewrite the whole expression; a column keeps the TTL fixed
  and changes only a default.
- **Materialising the column with `MATERIALIZED` instead of `DEFAULT`.** Same effect for new rows;
  `DEFAULT` is also computed lazily for parts written before the column existed, which is what makes
  existing data follow the rules without a backfill.

## Consequences

- Rules apply to rows as they are written. A part that already holds a stored `_retention_days` keeps
  it; parts written before migration 0069 compute it from the current rules until a merge rewrites
  them, after which that value is fixed. Changing a rule therefore doesn't retroactively shorten or
  lengthen data that has already been merged with an older rule set.
- `ALTER ... MODIFY COLUMN ... DEFAULT` on a column a TTL reads is accepted by ClickHouse and is
  metadata-only (verified on single-node and a 4-node cluster).
- A row with a timestamp after 2051 can overflow `DateTime` arithmetic and be deleted by any TTL. That
  was already true of fixed retention and is not new.
