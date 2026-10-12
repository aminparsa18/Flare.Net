# Retention and tiering

How does Flare decide when telemetry is deleted, how can old data be kept
cheaply instead, and what does that do to the queries you run? This page
explains the retention model: ClickHouse TTLs, the optional cold tier, and
per-resource rules, and how the three interact. For the steps, see
[Set data retention](../how-to/data/set-data-retention.md).

## Retention is a TTL that ClickHouse owns

By default Flare keeps every row forever; an upgrade never deletes data nobody
asked to delete. Setting retention does not start a Flare job that deletes
rows. It puts a **TTL** (time to live) on the raw telemetry tables, and
ClickHouse removes expired rows itself, in the background, while merging data
parts ([ADR-0143](../../docs-internal/adr/0143-retention-ttl.md)).

Retention is set per signal: logs, traces, metrics and profiles. A signal maps
to the tables behind it (traces cover spans and app screenshots, metrics cover
all four metric tables). `0` days means keep forever.

Why a TTL, and not a scheduled `DROP PARTITION` from Flare? Tables are
partitioned by month, so dropping partitions can only remove whole months, and
it makes Flare responsible for a job the database already does. A TTL is exact
to the row and keeps working while Flare is down.

Consequences to plan around:

- **Deletion is gradual.** Disk frees up over hours as merges touch old parts,
  not at the moment the change is reported as done. Flare applies the TTL
  without rewriting existing parts up front, so shortening retention on a large
  table doesn't trigger one huge mutation.
- **Shortening is destructive and can't be undone.**
- **The change is asynchronous and tracked.** The API accepts a change, records
  it as pending, and a background service applies it, so a slow cluster-wide
  `ALTER` can't time out an HTTP request. Only one change can be pending at a
  time. "Success" means the TTL is in place, not that the data is gone.
- **Actual and expected are shown separately.** The dashboard reads the TTL
  live from the database, so a TTL edited by hand shows up as custom instead of
  being hidden behind what Flare last wrote.
- **Rollups outlive their source.** The pre-aggregated tables behind the service
  map, SLOs and dashboards, and the configuration tables, have no TTL. They are
  small, and expiring a rollup before the raw data would make the two disagree.
  The flip side is that a rollup can show history whose raw spans are gone.

## The cold tier moves data instead of deleting it

Deleting is not always what you want. Last quarter's logs are rarely read but
costly on fast local disk. With cold storage, aged data moves to a second
ClickHouse disk backed by S3-compatible object storage (RustFS in the bundled
overlays), through a storage policy with a local volume and a `cold` volume.
The move is another rule on the same TTL
([ADR-0144](../../docs-internal/adr/0144-cold-storage-rustfs.md)).

The key design choice is that tiering is **ClickHouse's own, not an archive
Flare manages**. Cold rows stay in the same table, so every search, alert,
dashboard and SLO keeps working with no change. What differs is cost and speed:

- **Queries that touch cold data are slower**, because reads go to object
  storage. Queries confined to recent data stay on local disk and are
  unaffected.
- **Moves happen in the background**, over minutes to hours after you apply them.
  The move rule is evaluated by background work rather than on every insert, so
  it doesn't slow ingestion.
- **It is opt-in infrastructure.** The disk and policy are enabled by a compose
  overlay (or the Aspire option), not by default. Without them, asking for
  tiering is refused with an error rather than silently ignored.
- **Turning tiering off doesn't pull data back.** The move rule is removed, but
  parts already on the cold volume stay there. They are still read normally and
  still deleted by the delete rule.
- **Tier-only is possible.** With retention `0` and a cold-after age set, data
  moves to cold storage and is never deleted.
- **The cold disk's free space is informational**, since it describes an object
  store.

The cold-after age must be less than the retention, otherwise rows would be
deleted before they move. Archiving a *copy* of telemetry to S3 in an open
format is a separate feature, because archived data is not queryable in Flare
(see [Archive telemetry to S3](../how-to/data/archive-telemetry-to-s3.md)).

## Per-resource retention

One lifetime per signal is often too coarse: keep `deployment.environment=dev`
for a week and everything else for a month. A ClickHouse TTL is one expression
per table, so the lifetime has to belong to the row. Each raw table therefore
has a `_retention_days` column, and the TTL reads it
([ADR-0145](../../docs-internal/adr/0145-per-resource-retention.md)). Flare
turns your ordered rules into that column's default expression, so the value is
computed by ClickHouse when a row is inserted and ingestion is untouched.

How it behaves:

- **Rules are exact matches on a resource attribute**, evaluated in order, first
  match wins, with the signal's own retention as the fallback. Not prefixes or
  regexes, and not log or span attributes. There is a cap on rules per signal.
  This covers environment, namespace or team, and keeps the generated statements
  small enough to check.
- **Rules apply as rows are written.** A row's value is fixed once it has been
  merged. Changing a rule does not retroactively shorten or lengthen data that is
  already stored under the old rules. This is the main surprise: it is a policy
  for incoming data, not a re-evaluation of history.
- **"Forever" is a large number, not zero.** A zero-day TTL deletes immediately,
  and an enormous value overflows ClickHouse's date type and wraps into the
  past, which also deletes the row. The model uses a bounded value that is safe,
  and caps what you can type so the two can't be confused.
- **Cold tiering stays one age per signal**, and must be below the shortest finite
  retention among the rules and the default, for the same reason as above.

## How the pieces interact

| You set | Result |
| --- | --- |
| Retention only | Rows are deleted after that many days |
| Retention + cold-after | Parts move to cold storage first, then are deleted at the retention age |
| Cold-after only (retention 0) | Data moves to cold storage and is kept forever |
| Per-resource rules | Each row is deleted at its own lifetime; cold-after still applies to all of them at one age |

If something looks off, compare the live TTL on the Retention page with what was
last requested before assuming the data is gone.

## Related

- Explanation: [Sampling, clustering and cost](sampling-clustering-and-cost.md), [How Flare stores and queries telemetry](how-flare-stores-and-queries-telemetry.md), [Clustering](clustering.md)
- How-to: [Set data retention](../how-to/data/set-data-retention.md), [Archive telemetry to S3](../how-to/data/archive-telemetry-to-s3.md), [See what drives storage](../how-to/data/see-what-drives-storage.md)
- ADRs: [0143](../../docs-internal/adr/0143-retention-ttl.md), [0144](../../docs-internal/adr/0144-cold-storage-rustfs.md), [0145](../../docs-internal/adr/0145-per-resource-retention.md)
