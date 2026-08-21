namespace Flare.Api.Model;

/// <summary>
/// One ClickHouse table's storage footprint, read from <c>system.tables</c> (one row per
/// table already carries everything needed - <c>total_bytes</c>/<c>total_bytes_uncompressed</c>/
/// <c>active_parts</c> - no join against <c>system.parts</c> required; confirmed live that
/// <c>system.parts</c>' own <c>data_uncompressed_bytes</c> excludes index/mark overhead and
/// so reads *smaller* than the compressed size at small row counts - misleading for a
/// "compression ratio" stat - while <c>system.tables.total_bytes_uncompressed</c> doesn't
/// have that problem).
/// </summary>
public sealed record TableStorageInfo(
    string TableName,
    string Engine,
    string SortingKey,
    long Rows,
    long ActiveParts,
    long CompressedBytes,
    long UncompressedBytes);

/// <summary>One skip index (<c>system.data_skipping_indices</c>) - the schema-defined acceleration structures behind fast trace/attribute lookups.</summary>
public sealed record SkipIndexInfo(
    string TableName,
    string IndexName,
    string Type,
    string Expression,
    long Granularity,
    long CompressedBytes,
    long UncompressedBytes);

/// <summary>
/// One day's new-part bytes for one table, from <c>system.part_log</c> (<c>event_type = 'NewPart'</c>).
/// An approximation of ingestion growth, not exact disk delta - merges/mutations rewrite
/// parts (also logged, but as separate event types this doesn't count), so this reads high
/// relative to net disk growth on a table with heavy background merging. Good enough for
/// "is this table growing and how fast," not a byte-exact audit.
/// </summary>
public sealed record StorageGrowthPoint(DateTimeOffset Day, string TableName, long Bytes, long Rows);

/// <summary>
/// Disk backing ClickHouse's data, from <c>system.disks</c>. Self-hosted deployments have
/// no notion of a "configured storage limit" for us to compare against, so this stands in
/// for one - real headroom instead of a made-up quota. Picks the largest disk when more
/// than one is configured (default single-disk setups have exactly one row).
/// <see cref="Available"/> false means <c>system.disks</c> itself wasn't queryable (should
/// be rare - unlike part_log/query_log this table isn't config-gated - but still handled
/// defensively rather than failing the whole response over it).
/// </summary>
public sealed record DiskUsageInfo(bool Available, long TotalBytes, long FreeBytes);

/// <summary>
/// p95 duration of queries Flare.Api itself ran against <c>currentDatabase()</c> in the
/// trailing <see cref="WindowMinutes"/>, from <c>system.query_log</c> - "is the thing users
/// actually feel (searching logs, opening traces) fast" rather than an index/storage count
/// nobody can act on. Excludes queries that also touch <c>system</c> tables so this page's
/// own three introspection queries don't pollute the number.
/// </summary>
/// <remarks>
/// Config-gated like <see cref="StorageGrowthPoint"/>'s <c>system.part_log</c> -
/// <see cref="Available"/> is false when <c>system.query_log</c> isn't queryable on this
/// deployment. When it is, <see cref="P95Ms"/> is still null if <see cref="SampleCount"/>
/// is zero (queryable, just no query traffic in the window) - the dashboard tells those two
/// "nothing to show" cases apart rather than collapsing both into one em dash.
/// </remarks>
public sealed record QueryPerformanceInfo(bool Available, double? P95Ms, long SampleCount, int WindowMinutes);

/// <summary>
/// <c>GET /api/indexing/stats</c> response. <see cref="GrowthAvailable"/> is false when
/// <c>system.part_log</c> doesn't exist or isn't queryable (it's config-gated, not
/// guaranteed on every ClickHouse deployment) - the dashboard shows a plain note instead
/// of an empty chart in that case, rather than the endpoint failing outright.
/// </summary>
public sealed record IndexingStatsResponse(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<TableStorageInfo> Tables,
    IReadOnlyList<SkipIndexInfo> SkipIndexes,
    IReadOnlyList<StorageGrowthPoint> Growth,
    bool GrowthAvailable,
    DiskUsageInfo DiskUsage,
    QueryPerformanceInfo QueryPerformance);
