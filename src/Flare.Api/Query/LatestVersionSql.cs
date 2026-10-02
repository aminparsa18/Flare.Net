namespace Flare.Api.Query;

/// <summary>
/// Builds the read SQL for the versioned config tables (<c>alert_rules</c>,
/// <c>notification_channels</c>, <c>maintenance_windows</c>, <c>saved_views</c>,
/// <c>dashboards</c>, <c>pipeline_rules</c>) - each a <c>ReplacingMergeTree(UpdatedAt)</c>
/// where every create/update/delete INSERTs a new version row for the same <c>Id</c>.
/// </summary>
/// <remarks>
/// <para>
/// Picks the latest version per <c>Id</c> at query time
/// (<c>ORDER BY UpdatedAt DESC LIMIT 1 BY Id</c>) instead of relying on <c>FINAL</c>. In
/// cluster mode these tables' <c>Distributed</c> counterparts shard by <c>rand()</c>
/// (ADR-0003), so one <c>Id</c>'s versions can land on different shards, and <c>FINAL</c>
/// only collapses versions within a shard - a <c>FINAL</c> read then returns one row per
/// shard and the caller takes whichever arrives first, i.e. sometimes a stale version.
/// <c>LIMIT 1 BY</c> is applied after the initiator merges every shard's rows, so it
/// sees all versions. Re-sharding by <c>cityHash64(Id)</c> was rejected: a sharding-key
/// change doesn't move rows already written under <c>rand()</c>. These tables hold tens
/// to hundreds of rows, so reading every version is cheap. Single-node mode gets the
/// same answer <c>FINAL</c> gave.
/// </para>
/// <para>
/// <c>IsDeleted = 0</c> and <paramref name="latestWhere"/> filter the <em>latest</em>
/// version, not every version - filtering first would let a tombstoned or disabled row
/// fall back to its previous live/enabled version. Only predicates on fields that never
/// change across versions (the <c>Id</c> itself) may go in <paramref name="idWhere"/>,
/// which is pushed into the inner scan.
/// </para>
/// </remarks>
public static class LatestVersionSql
{
    public static string Select(string table, string columns, string? idWhere = null, string? latestWhere = null, string? orderBy = null)
    {
        var inner = $"SELECT {columns}, IsDeleted FROM {table}"
            + (idWhere is null ? "" : $" WHERE {idWhere}")
            + " ORDER BY UpdatedAt DESC LIMIT 1 BY Id";
        return $"SELECT {columns} FROM ({inner}) WHERE IsDeleted = 0"
            + (latestWhere is null ? "" : $" AND {latestWhere}")
            + (orderBy is null ? "" : $" ORDER BY {orderBy}");
    }
}
