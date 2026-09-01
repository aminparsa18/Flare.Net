using MemoryPack;

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
[MemoryPackable]
public sealed partial record TableStorageInfo(
    string TableName,
    string Engine,
    string SortingKey,
    long Rows,
    long ActiveParts,
    long CompressedBytes,
    long UncompressedBytes);

/// <summary>One skip index (<c>system.data_skipping_indices</c>) - the schema-defined acceleration structures behind fast trace/attribute lookups.</summary>
[MemoryPackable]
public sealed partial record SkipIndexInfo(
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
[MemoryPackable]
public sealed partial record StorageGrowthPoint(DateTimeOffset Day, string TableName, long Bytes, long Rows);

/// <summary>
/// Disk backing ClickHouse's data, from <c>system.disks</c>. Self-hosted deployments have
/// no notion of a "configured storage limit" for us to compare against, so this stands in
/// for one - real headroom instead of a made-up quota. Picks the largest disk when more
/// than one is configured (default single-disk setups have exactly one row).
/// <see cref="Available"/> false means <c>system.disks</c> itself wasn't queryable (should
/// be rare - unlike part_log/query_log this table isn't config-gated - but still handled
/// defensively rather than failing the whole response over it).
/// </summary>
[MemoryPackable]
public sealed partial record DiskUsageInfo(bool Available, long TotalBytes, long FreeBytes);

/// <summary>
/// Latency of queries Flare.Api itself ran against <c>currentDatabase()</c> in the
/// trailing <see cref="WindowMinutes"/>, from <c>system.query_log</c> - "is the thing users
/// actually feel (searching logs, opening traces) fast" rather than an index/storage count
/// nobody can act on. Excludes queries that also touch <c>system</c> tables so this page's
/// own introspection queries (this class's own reads included) don't pollute the numbers.
/// Backs both the Indexing page's "Query performance" summary card (p95 only) and its
/// "Query optimization" section (all three percentiles + the slow-query count).
/// </summary>
/// <remarks>
/// Config-gated like <see cref="StorageGrowthPoint"/>'s <c>system.part_log</c> -
/// <see cref="Available"/> is false when <c>system.query_log</c> isn't queryable on this
/// deployment. When it is, the percentiles are still null if <see cref="SampleCount"/> is
/// zero (queryable, just no query traffic in the window) - the dashboard tells those two
/// "nothing to show" cases apart rather than collapsing both into one em dash.
/// </remarks>
[MemoryPackable]
public sealed partial record QueryPerformanceInfo(
    bool Available,
    double? P50Ms,
    double? P95Ms,
    double? P99Ms,
    long SlowQueryCount,
    long SampleCount,
    int WindowMinutes,
    int SlowQueryThresholdMs);

/// <summary>
/// <c>GET /api/indexing/stats</c> response. <see cref="GrowthAvailable"/> is false when
/// <c>system.part_log</c> doesn't exist or isn't queryable (it's config-gated, not
/// guaranteed on every ClickHouse deployment) - the dashboard shows a plain note instead
/// of an empty chart in that case, rather than the endpoint failing outright.
/// </summary>
[MemoryPackable]
public sealed partial record IndexingStatsResponse(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<TableStorageInfo> Tables,
    IReadOnlyList<SkipIndexInfo> SkipIndexes,
    IReadOnlyList<StorageGrowthPoint> Growth,
    bool GrowthAvailable,
    DiskUsageInfo DiskUsage,
    QueryPerformanceInfo QueryPerformance);

/// <summary>
/// One row of <c>system.clusters</c> for the <c>flare_cluster</c> cluster, joined with that
/// same node's own <c>system.replicas</c> state - one ClickHouse node's place in the
/// shard/replica topology, plus whether it's actually caught up. <see cref="ErrorsCount"/>/
/// <see cref="EstimatedRecoveryTimeSeconds"/> are the connecting node's own view of that
/// peer (ClickHouse tracks these per-connection, not as a cluster-wide consensus) - good
/// enough for "can I reach this node right now," but deliberately NOT a signal about
/// replication currency: a node can show zero errors while being arbitrarily far behind on
/// applying its replication queue. <see cref="ReplicationQueueSize"/>/
/// <see cref="ReplicationLagSeconds"/> are that missing signal - summed/maxed across this
/// node's replicated tables (<c>queue_size</c>/<c>absolute_delay</c> from
/// <c>system.replicas</c>) - and are the fields that actually answer "is this replica
/// synchronized." Both are 0 when <see cref="ClusterStatusResponse.ReplicationInfoAvailable"/>
/// is false; the dashboard must check that flag rather than trusting a bare 0 here, for the
/// same reason <see cref="ClusterQueryService"/>'s remarks call out. See
/// <see cref="ClusterQueryService"/>'s remarks for what this still doesn't cover (Keeper
/// quorum health).
/// </summary>
[MemoryPackable]
public sealed partial record ClusterNodeInfo(
    int ShardNum,
    int ReplicaNum,
    string HostName,
    int Port,
    bool IsLocal,
    long ErrorsCount,
    long EstimatedRecoveryTimeSeconds,
    long ReplicationQueueSize,
    long ReplicationLagSeconds);

/// <summary>
/// <c>GET /api/indexing/cluster</c> response - backs the Indexing page's cluster-status
/// panel (Planning.md's "Multi-node scaling" follow-up, docs/clustering.md). When
/// <see cref="ClusterModeEnabled"/> is false (the default, single-node deployment),
/// <see cref="Nodes"/> is always empty and no ClickHouse query even runs - see
/// <see cref="ClusterQueryService"/>. <see cref="ReplicationInfoAvailable"/> is false when
/// the <c>system.replicas</c> read failed (degrades independently of the topology read -
/// see <see cref="ClusterQueryService"/>) - every node's <see cref="ClusterNodeInfo.ReplicationQueueSize"/>/
/// <see cref="ClusterNodeInfo.ReplicationLagSeconds"/> is then a placeholder 0, not a real
/// "caught up" reading, and must be rendered as unknown rather than healthy.
/// </summary>
[MemoryPackable]
public sealed partial record ClusterStatusResponse(
    bool ClusterModeEnabled,
    bool SharedPatternStoreEnabled,
    bool ReplicationInfoAvailable,
    IReadOnlyList<ClusterNodeInfo> Nodes);
