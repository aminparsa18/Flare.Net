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
/// Topology comes from a single query, no cluster()/clusterAllReplicas() branching the way
/// <see cref="IndexingQueryService"/>'s queries have - <c>system.clusters</c> is each
/// node's local copy of <c>remote-servers.xml</c>, identical on all 4 nodes regardless of
/// which one <c>clickhouse-lb</c> happens to route the request to, so there's nothing to
/// aggregate across shards/replicas the way storage or query-log stats need.
///
/// <para>
/// <b>Replication lag/queue</b> (<see cref="ClusterNodeInfo.ReplicationQueueSize"/>/
/// <see cref="ClusterNodeInfo.ReplicationLagSeconds"/>) is a second, independent query
/// against <c>system.replicas</c> - the signal <c>errors_count</c> above can't provide,
/// since it's reachability, not replication currency. Uses
/// <c>clusterAllReplicas('flare_cluster', system.replicas)</c> (node-local state, same
/// category as <see cref="IndexingQueryService"/>'s disk-usage/query-performance reads, not
/// the per-shard-summed ones) grouped by <c>hostName()</c>, then joined in C# against the
/// topology query's <c>host_name</c> - requires each ClickHouse container's actual OS
/// hostname to equal its Compose service name, which is NOT Docker Compose's default (a
/// container's hostname defaults to a random container ID unless set explicitly).
/// <c>docker-compose.cluster.yml</c> sets <c>hostname: clickhouse-N</c> explicitly on each
/// of the 4 services for exactly this join - confirmed live (2026-08-30) that without it,
/// <c>hostName()</c> returns the container ID instead and the join silently matches
/// nothing, degrading every node to "unavailable" rather than throwing. A node can have
/// multiple replicated tables, so <c>max(absolute_delay)</c>/<c>sum(queue_size)</c> collapse
/// them to one lag/queue figure per node - "the most any single table on this node is
/// behind," not an average that could hide one badly-lagging table behind several
/// caught-up ones. <c>absolute_delay</c> is <c>UInt64</c> on this ClickHouse version (also
/// confirmed live, after an initial <c>UInt32</c> read threw <c>InvalidCastException</c> on
/// every row) - same "type doesn't match what the docs imply" trap
/// <see cref="ClusterNodeInfo.EstimatedRecoveryTimeSeconds"/> hit the other direction.
/// </para>
/// <para>
/// <b>Deliberately out of scope for this pass</b> (named here rather than silently
/// skipped): ClickHouse Keeper quorum health - <c>system.clusters</c> has no notion of it
/// at all, Keeper speaks its own four-letter-word protocol (<c>mntr</c>/<c>ruok</c>, see
/// this repo's own healthchecks in <c>docker-compose.cluster.yml</c>) over a completely
/// separate connection, not SQL through <see cref="IClickHouseClient"/> - a real
/// implementation needs its own client and is a bigger unit of work than this panel's
/// first cut. Also out of scope: <c>system.replication_queue</c>'s own per-entry detail
/// (what specifically is stuck, e.g. a retrying merge) - <c>system.replicas</c>' aggregate
/// counters above are enough for "is this node caught up," not "why isn't it."
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

    // One row per node - see this class's own remarks for why max()/sum() here, and why
    // hostName() is safe to join against system.clusters.host_name.
    private const string ReplicationLagSql = """
        SELECT hostName() AS host, max(absolute_delay) AS max_absolute_delay, sum(queue_size) AS total_queue_size
        FROM clusterAllReplicas('flare_cluster', system.replicas)
        WHERE database = currentDatabase()
        GROUP BY host
        """;

    public async Task<ClusterStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        // No ClickHouse round trip at all for the common (single-node) case - avoids
        // paying for a query against a cluster name that plain docker-compose.yml's
        // ClickHouse never defines in the first place.
        if (!clusterMode)
        {
            return new ClusterStatusResponse(ClusterModeEnabled: false, sharedPatternStoreEnabled, ReplicationInfoAvailable: false, Nodes: []);
        }

        var nodesTask = ReadNodesAsync(cancellationToken);
        var replicationTask = ReadReplicationLagAsync(cancellationToken);
        await Task.WhenAll(nodesTask, replicationTask);

        var nodes = await nodesTask;
        var (lagByHost, replicationAvailable) = await replicationTask;

        // Independent read from ReadNodesAsync - a node with no matching row (replication
        // read failed entirely, or genuinely has no replicated tables) just keeps the 0
        // default already on the record; ReplicationInfoAvailable is the flag callers must
        // check before trusting that 0 as "caught up" rather than "unknown".
        var mergedNodes = nodes
            .Select(node => lagByHost.TryGetValue(node.HostName, out var lag)
                ? node with { ReplicationQueueSize = lag.QueueSize, ReplicationLagSeconds = lag.AbsoluteDelaySeconds }
                : node)
            .ToList();

        return new ClusterStatusResponse(ClusterModeEnabled: true, sharedPatternStoreEnabled, replicationAvailable, mergedNodes);
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
                    EstimatedRecoveryTimeSeconds: reader.GetFieldValue<uint>(6),
                    ReplicationQueueSize: 0,
                    ReplicationLagSeconds: 0));
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

    /// <summary>
    /// Failure here degrades independently of <see cref="ReadNodesAsync"/> - the topology
    /// can still render with replication columns marked unavailable rather than the whole
    /// panel going empty over a <c>system.replicas</c> hiccup.
    /// </summary>
    private async Task<(Dictionary<string, (long QueueSize, long AbsoluteDelaySeconds)> LagByHost, bool Available)> ReadReplicationLagAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var reader = await client.ExecuteReaderAsync(ReplicationLagSql, null, SafetyOptions(), cancellationToken);
            var lagByHost = new Dictionary<string, (long QueueSize, long AbsoluteDelaySeconds)>();
            while (reader.Read())
            {
                lagByHost[reader.GetString(0)] = (
                    QueueSize: (long)reader.GetFieldValue<ulong>(2),
                    AbsoluteDelaySeconds: (long)reader.GetFieldValue<ulong>(1));
            }

            return (lagByHost, true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cluster status's replication lag unavailable - system.replicas wasn't queryable against the 'flare_cluster' cluster");
            return ([], false);
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
