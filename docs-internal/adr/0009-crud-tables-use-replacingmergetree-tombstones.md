# ADR-0009: "Config, not log data" tables use `ReplacingMergeTree` + tombstone rows, not `ALTER ... UPDATE/DELETE`

Status: Accepted
Date: 2026-08-09 (`alert_rules`, migration `0003`) — reused verbatim for
`saved_views` (migration `0009`, 2026-08-10)

## Context

Most of Flare's schema (`logs`, `spans`, `metrics_*`) is write-once,
immutable event data, a natural fit for plain `MergeTree`. But two tables
are different in kind: `alert_rules` (CRUD'd through the dashboard/API —
create, edit, delete, list) and later `saved_views` (the same shape).
Normal CRUD needs update and delete semantics that a plain `MergeTree` has
no good native story for — ClickHouse's `ALTER TABLE ... UPDATE/DELETE`
are async background mutations, the wrong fit for "write, then read back
immediately," which is exactly what a dashboard CRUD form expects.

## Decision

For any table that is CRUD'd rather than append-only, use
**`ReplacingMergeTree(UpdatedAt)`** with a tombstone column
(`IsDeleted`): every create/update inserts a new row for the same `Id`;
delete inserts a tombstone row (`IsDeleted = 1`) instead of removing
anything. Reads always go through `FROM <table> FINAL WHERE IsDeleted = 0`.
This pattern was established with `alert_rules` and reused **verbatim**
for `saved_views` — same tombstone column, same `FINAL` read pattern, same
accepted cost trade-off.

## Alternatives considered

- **`ALTER TABLE ... UPDATE`/`DELETE` mutations.** Rejected: these are
  asynchronous background operations in ClickHouse, not transactional,
  synchronous statements — wrong fit for a table backing an interactive
  CRUD UI where a save needs to be immediately visible on the next read.
- **A genuinely mutable store for these tables specifically** (e.g. the
  identity SQLite database, see ADR-0004) instead of ClickHouse. Not
  recorded as seriously weighed in the source material — `alert_rules`/
  `saved_views` are query-domain data (referenced by, and filtered
  alongside, log/trace/metric queries), unlike identity/auth data, so
  keeping them in ClickHouse alongside the data they operate over was the
  natural fit; SQLite was reserved for identity/auth's different access
  pattern.

## Consequences

- **`FINAL`'s per-part merge cost is accepted**, given the expected row
  count for these tables — a self-hosted instance has tens to low hundreds
  of alert rules or saved views, nothing like `logs`' volume. The
  reviewed, not-yet-needed fallback if that assumption stops holding is
  `GROUP BY Id ORDER BY UpdatedAt DESC LIMIT 1 BY Id` instead of `FINAL`.
- Row content for these tables is stored as **opaque JSON** rather than
  exploded into columns (`alert_rules.ConditionJson`,
  `saved_views.StateJson`) — a direct consequence of this pattern being
  about CRUD semantics, not query semantics: these tables never need to
  filter/aggregate on their own condition/state contents in SQL, only
  round-trip them through the application layer. `saved_views.StateJson`
  goes further than `alert_rules.ConditionJson` — it's fully opaque even
  to `Flare.Api` (owned entirely by the dashboard's TypeScript), whereas
  `ConditionJson` at least deserializes into a real `LogFilter` C# model.
- **This is now the established pattern for any future CRUD-shaped table**
  in this schema — a new table needing create/update/delete semantics
  should reach for `ReplacingMergeTree(UpdatedAt)` + tombstone + `FINAL`
  rather than re-deriving a different approach, unless its row-count
  profile is expected to invalidate the `FINAL`-cost assumption above.
- ClickHouse has no `CHECK` constraint story that fits this insert-a-new-
  version-per-update shape, so cross-column validation (e.g.
  `alert_rules`' "exactly one notification channel set") is enforced at
  the application layer (`AlertRuleRequest.ValidateChannel`), not the
  schema — a consequence worth knowing before assuming the schema itself
  guards against invalid rows.

## Related documentation

- `db/clickhouse/README.md` — the full migration history this pattern
  spans (`0003_alert_rules.sql` through `0009_saved_views.sql`)