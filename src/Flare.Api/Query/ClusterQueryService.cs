using ClickHouse.Driver;
using Flare.Api.Model;

namespace Flare.Api.Query;

public interface IClusterStatusService
{
    Task<ClusterStatusResponse> GetStatusAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Backs the Indexing page's cluster-status panel - the dashboard-facing follow-up named
/// in Planning.md's "Multi-node scaling" item once every limitation docs/clustering.md
/// used to track was closed out. Lives alongside <see cref="IndexingQueryService"/> (same
/// page, same <c>ClickHouse:ClusterMode</c> flag) rather than folded into it - this is a
/// distinct data shape (one row per node, not an aggregate stat) on a distinct cadence
/// (topology barely changes; the dashboard still re-fetches on every manual refresh the
/// same as the rest of the page, just via a separate request).
/// </summary>
/// <remarks>
/// Single query, no cluster()/clusterAllReplicas() branching the way
/// <see cref="IndexingQueryService"/>'s queries have - <c>system.clusters</c> is each
/// node's local copy of <c>remote-servers.xml</c>, identical on all 4 nodes regardless of
/// which one <c>clickhouse-lb</c> happens to route the request to, so there's nothing to
/// aggregate across shards/replicas the way storage or query-log stats need.
///
/// <para>
/// <b>Deliberately out of scope for this pass</b> (named here rather than silently
/// skipped): ClickHouse Keeper quorum health - <c>system.clusters</c> has no notion of it
/// at all, Keeper speaks its own four-letter-word protocol (<c>mntr</c>/<c>ruok</c>, see
/// this repo's own healthchecks in <c>docker-compose.cluster.yml</c>) over a completely
/// separate connection, not SQL through <see cref="IClickHouseClient"/> - a real
/// implementation needs its own client and is a bigger unit of work than this panel's
/// first cut. Also out of scope: replication queue/lag (<c>system.replication_queue</c>) -
/// worth adding later, not attempted here.
/// </para>
/// </remarks>
public sealed class ClusterQueryService(
    IClickHouseClient client,
    ILogger<ClusterQueryService> logger,
    bool clusterMode,
    bool sharedPatternStoreEnabled) : IClusterStatusService
{
    // Matches the literal 'flare_cluster' name defined in db/clickhouse-cluster's
    // remote-servers.xml - same hand-synced-literal spirit as IndexingQueryService's own
    // copy of this constant (no shared constant between them, kept consistent by hand).
    private const string ClusterNodesSql = """
        SELECT shard_num, replica_num, host_name, port, is_local, errors_count, estimated_recovery_time
        FROM system.clusters
        WHERE cluster = 'flare_cluster'
        ORDER BY shard_num, replica_num
        """;

    public async Task<ClusterStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        // No ClickHouse round trip at all for the common (single-node) case - avoids
        // paying for a query against a cluster name that plain docker-compose.yml's
        // ClickHouse never defines in the first place.
        if (!clusterMode)
        {
            return new ClusterStatusResponse(ClusterModeEnabled: false, sharedPatternStoreEnabled, Nodes: []);
        }

        var nodes = await ReadNodesAsync(cancellationToken);
        return new ClusterStatusResponse(ClusterModeEnabled: true, sharedPatternStoreEnabled, nodes);
    }

    private async Task<IReadOnlyList<ClusterNodeInfo>> ReadNodesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var reader = await client.ExecuteReaderAsync(ClusterNodesSql, null, SafetyOptions(), cancellationToken);
            var nodes = new List<ClusterNodeInfo>();
            while (reader.Read())
            {
                nodes.Add(new ClusterNodeInfo(
                    ShardNum: (int)reader.GetFieldValue<uint>(0),
                    ReplicaNum: (int)reader.GetFieldValue<uint>(1),
                    HostName: reader.GetString(2),
                    Port: reader.GetFieldValue<ushort>(3),
                    IsLocal: reader.GetFieldValue<byte>(4) != 0,
                    ErrorsCount: reader.GetFieldValue<uint>(5),
                    EstimatedRecoveryTimeSeconds: reader.GetFieldValue<uint>(6)));
            }

            return nodes;
        }
        catch (Exception ex)
        {
            // Degrades to an empty node list rather than failing the whole page - same
            // "config-gated system table might not cooperate" posture as
            // IndexingQueryService's growth/query-performance reads, even though
            // system.clusters isn't actually config-gated the way part_log/query_log are;
            // a transient connectivity blip mid-request shouldn't take the Indexing page
            // down with it.
            logger.LogWarning(ex, "Cluster status unavailable - system.clusters wasn't queryable against the 'flare_cluster' cluster");
            return [];
        }
    }

    /// <summary>Same query-safety rationale as <see cref="IndexingQueryService"/>'s own copy.</summary>
    private static QueryOptions SafetyOptions() => new()
    {
        CustomSettings = new Dictionary<string, object>
        {
            ["max_execution_time"] = 30,
            ["timeout_before_checking_execution_speed"] = 0,
        },
    };
}
