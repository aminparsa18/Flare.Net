# ADR-0074: Config tables read the latest version per `Id`, not `FINAL`

Status: Accepted

Date: 2026-10-02

Supersedes the "reads always go through `FROM <table> FINAL WHERE IsDeleted = 0`"
part of [ADR-0009](0009-crud-tables-use-replacingmergetree-tombstones.md). The
rest of ADR-0009 (`ReplacingMergeTree(UpdatedAt)`, insert-a-version-per-edit,
tombstone deletes) is unchanged.

## Context

`alert_rules`, `notification_channels`, `maintenance_windows`, `saved_views`,
`dashboards` and `pipeline_rules` store one row per edit and read the latest
with `FINAL`. In cluster mode their `Distributed` tables shard by `rand()`
([ADR-0003](0003-distributed-tables-plain-names-and-sharding.md)), so the
versions of one `Id` can land on different shards. `FINAL` only collapses
versions within a shard. Once versions span shards, a read returns one row
per shard and the caller keeps whichever arrives first.

This was seen live: an edited dashboard alternated between its old and new
names across reloads. Reproduced on the 2×2 cluster by writing v1 of a
dashboard to shard 1 and v2 to shard 2, plus a live row on shard 1 whose
tombstone is on shard 2. `FINAL` returned both versions and the deleted
dashboard, from all four nodes. For alert and pipeline rules the same split
means an edited threshold, or a disabled rule, can keep being evaluated in
its old form.

An earlier note
([clickhouse-cluster-operational-notes.md](../investigations/clickhouse-cluster-operational-notes.md),
finding 5) recorded `FINAL` over `Distributed` as correct. That test's two
versions happened to share a shard.

## Decision

Every read of these tables picks the latest version per `Id` across the
whole `Distributed` table, then applies `IsDeleted = 0` and any other filter
to that version:

```sql
SELECT <cols> FROM (
    SELECT <cols>, IsDeleted FROM <table> [WHERE Id ...]
    ORDER BY UpdatedAt DESC LIMIT 1 BY Id
) WHERE IsDeleted = 0 [AND <filter>] [ORDER BY ...]
```

`LIMIT 1 BY` runs on the initiator after it merges every shard's rows, so it
sees all versions. `Flare.Api` builds this with `LatestVersionSql.Select`;
`Flare.Ingest`'s `ClickHousePipelineRuleStore` spells the same query out by
hand (the two projects don't share types).

Filters on mutable fields (`Enabled`, `PageType`) go outside the subquery.
Inside, they would let a disabled or deleted row fall back to its older
enabled version. Only `Id` predicates are pushed into the inner scan.

## Alternatives considered

- **Shard these tables by `cityHash64(Id)`** so all versions of a row share a
  shard and `FINAL` works. Rejected: a sharding-key change only routes new
  inserts. Rows already written under `rand()` stay split, so existing
  clusters would keep returning stale versions until their volumes were
  rebuilt. ADR-0003 already treats sharding-key changes as a correctness
  hazard for existing data.
- **`argMax(<col>, UpdatedAt) ... GROUP BY Id`.** Equivalent result, but
  every column needs its own `argMax`, which is noisier across six column
  lists of up to 27 columns.
- **`FINAL` with `LIMIT 1 BY` on top.** Works, but `FINAL` adds a per-shard
  merge that the `LIMIT 1 BY` already makes redundant.

## Consequences

- Fixes existing cluster data with no migration and no volume rebuild.
- Every read scans all versions of every row instead of FINAL-merged parts.
  These tables hold tens to hundreds of rows, so this is negligible. ADR-0009
  already named this query as the fallback.
- Single-node results are unchanged.
- If two versions of one `Id` share the exact same `UpdatedAt` (millisecond
  precision), which one wins is unspecified. `ReplacingMergeTree` would keep
  the last inserted. Not a practical concern for UI-driven edits.
- A new versioned config table should read through `LatestVersionSql`, not
  `FINAL`.
