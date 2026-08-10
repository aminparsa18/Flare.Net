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
    bool GrowthAvailable);
