using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Readers;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IKubernetesInventoryQueryService
{
    Task<KubernetesNodeListResponse> ListNodesAsync(KubernetesNodeListRequest request, CancellationToken cancellationToken);

    Task<KubernetesNodeMetricsResponse> GetNodeMetricsAsync(KubernetesNodeMetricsRequest request, CancellationToken cancellationToken);

    Task<KubernetesPodListResponse> ListPodsAsync(KubernetesPodListRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for the Kubernetes page's
/// Nodes/Pods tables - see <see cref="KubernetesInventoryQueryBuilder"/> for the SQL. Same
/// two-statement list shape and ordinal-read style as <see cref="HostInventoryQueryService"/>.
/// </summary>
public sealed class KubernetesInventoryQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IKubernetesInventoryQueryService
{
    public async Task<KubernetesNodeListResponse> ListNodesAsync(KubernetesNodeListRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = HostInventoryQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var now = timeProvider.GetUtcNow();

        var listSql = KubernetesInventoryQueryBuilder.BuildNodeList(request, windowMinutes, now);
        var nodes = new List<KubernetesNodeSummary>();
        await using (var reader = await client.ExecuteReaderAsync(listSql.Sql, listSql.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                nodes.Add(new KubernetesNodeSummary
                {
                    NodeName = reader.GetString(0),
                    ClusterName = NullIfEmpty(reader.GetString(1)),
                    LastSeen = ReadUtc(reader, 2),
                });
            }
        }

        var truncated = Truncate(nodes, KubernetesInventoryQueryBuilder.MaxNodes);
        if (nodes.Count == 0)
        {
            return new KubernetesNodeListResponse { WindowMinutes = windowMinutes, Nodes = nodes, Truncated = false };
        }

        var valuesSql = KubernetesInventoryQueryBuilder.BuildNodeValues(nodes.ConvertAll(n => n.NodeName), windowMinutes, now);
        var byNode = nodes.ToDictionary(n => n.NodeName, StringComparer.Ordinal);
        var allocatable = new Dictionary<string, double>(StringComparer.Ordinal);
        await using (var reader = await client.ExecuteReaderAsync(valuesSql.Sql, valuesSql.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                var node = reader.GetString(0);
                if (!byNode.TryGetValue(node, out var summary) || ReadFigure(reader) is not { } value)
                {
                    continue;
                }

                switch (reader.GetString(1))
                {
                    case KubernetesInventoryQueryBuilder.CpuKind: byNode[node] = summary with { CpuCores = value }; break;
                    case KubernetesInventoryQueryBuilder.MemoryKind: byNode[node] = summary with { MemoryWorkingSetBytes = value }; break;
                    case KubernetesInventoryQueryBuilder.MemoryPercentKind: byNode[node] = summary with { MemoryPercent = value }; break;
                    case KubernetesInventoryQueryBuilder.ReadyKind: byNode[node] = summary with { Ready = KubernetesInventoryQueryBuilder.DecodeReady(value) }; break;
                    case KubernetesInventoryQueryBuilder.PodCountKind: byNode[node] = summary with { PodCount = (int)value }; break;
                    case KubernetesInventoryQueryBuilder.AllocatableCpuKind: allocatable[node] = value; break;
                }
            }
        }

        return new KubernetesNodeListResponse
        {
            WindowMinutes = windowMinutes,
            Nodes = nodes.ConvertAll(n =>
            {
                var summary = byNode[n.NodeName];
                return summary with { CpuPercent = KubernetesInventoryQueryBuilder.CpuPercent(summary.CpuCores, allocatable.GetValueOrDefault(n.NodeName)) };
            }),
            Truncated = truncated,
        };
    }

    public async Task<KubernetesNodeMetricsResponse> GetNodeMetricsAsync(KubernetesNodeMetricsRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = HostInventoryQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var bucketWidthSeconds = HostInventoryQueryBuilder.BucketWidthSecondsFor(windowMinutes);
        var built = KubernetesInventoryQueryBuilder.BuildNodeMetrics(request.NodeName, windowMinutes, bucketWidthSeconds, timeProvider.GetUtcNow());

        var byBucket = new SortedDictionary<DateTimeOffset, KubernetesNodeMetricsPoint>();
        // Allocatable CPU per bucket, but only the latest is used: it's a node property that
        // changes rarely, and applying one denominator keeps every bucket's % comparable.
        (DateTimeOffset Bucket, double Value)? latestAllocatable = null;
        await using (var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                if (ReadFigure(reader) is not { } value)
                {
                    continue;
                }

                var bucket = ReadUtc(reader, 0);
                var kind = reader.GetString(1);
                if (kind == KubernetesInventoryQueryBuilder.AllocatableCpuKind)
                {
                    if (latestAllocatable is null || bucket > latestAllocatable.Value.Bucket)
                    {
                        latestAllocatable = (bucket, value);
                    }

                    continue;
                }

                var point = byBucket.GetValueOrDefault(bucket) ?? new KubernetesNodeMetricsPoint { BucketStart = bucket };
                byBucket[bucket] = kind switch
                {
                    KubernetesInventoryQueryBuilder.CpuKind => point with { CpuCores = value },
                    KubernetesInventoryQueryBuilder.MemoryKind => point with { MemoryWorkingSetBytes = value },
                    KubernetesInventoryQueryBuilder.MemoryPercentKind => point with { MemoryPercent = value },
                    _ => point,
                };
            }
        }

        var allocatableCpu = latestAllocatable?.Value;
        return new KubernetesNodeMetricsResponse
        {
            NodeName = request.NodeName,
            WindowMinutes = windowMinutes,
            BucketWidthSeconds = bucketWidthSeconds,
            AllocatableCpuCores = allocatableCpu,
            Points = [.. byBucket.Values.Select(p => p with { CpuPercent = KubernetesInventoryQueryBuilder.CpuPercent(p.CpuCores, allocatableCpu) })],
        };
    }

    public async Task<KubernetesPodListResponse> ListPodsAsync(KubernetesPodListRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = HostInventoryQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var now = timeProvider.GetUtcNow();

        var listSql = KubernetesInventoryQueryBuilder.BuildPodList(request, windowMinutes, now);
        var pods = new List<KubernetesPodSummary>();
        await using (var reader = await client.ExecuteReaderAsync(listSql.Sql, listSql.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                pods.Add(new KubernetesPodSummary
                {
                    Namespace = reader.GetString(0),
                    PodName = reader.GetString(1),
                    NodeName = NullIfEmpty(reader.GetString(2)),
                    WorkloadKind = NullIfEmpty(reader.GetString(3)),
                    WorkloadName = NullIfEmpty(reader.GetString(4)),
                    LastSeen = ReadUtc(reader, 5),
                });
            }
        }

        var truncated = Truncate(pods, KubernetesInventoryQueryBuilder.MaxPods);
        if (pods.Count == 0)
        {
            return new KubernetesPodListResponse { WindowMinutes = windowMinutes, Pods = pods, Truncated = false };
        }

        static string KeyOf(KubernetesPodSummary p) => KubernetesInventoryQueryBuilder.PodKey(p.Namespace, p.PodName);

        var valuesSql = KubernetesInventoryQueryBuilder.BuildPodValues(pods.ConvertAll(KeyOf), windowMinutes, now);
        var byKey = pods.ToDictionary(KeyOf, StringComparer.Ordinal);
        await using (var reader = await client.ExecuteReaderAsync(valuesSql.Sql, valuesSql.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                var key = reader.GetString(0);
                if (byKey.TryGetValue(key, out var summary) && ReadFigure(reader) is { } value)
                {
                    byKey[key] = reader.GetString(1) switch
                    {
                        KubernetesInventoryQueryBuilder.CpuKind => summary with { CpuCores = value },
                        KubernetesInventoryQueryBuilder.MemoryKind => summary with { MemoryWorkingSetBytes = value },
                        KubernetesInventoryQueryBuilder.CpuLimitKind => summary with { CpuLimitPercent = value },
                        KubernetesInventoryQueryBuilder.MemoryLimitKind => summary with { MemoryLimitPercent = value },
                        KubernetesInventoryQueryBuilder.PhaseKind => summary with { Phase = KubernetesInventoryQueryBuilder.DecodePhase(value) },
                        KubernetesInventoryQueryBuilder.RestartsKind => summary with { Restarts = (int)value },
                        _ => summary,
                    };
                }
            }
        }

        return new KubernetesPodListResponse
        {
            WindowMinutes = windowMinutes,
            Pods = pods.ConvertAll(p => byKey[KeyOf(p)]),
            Truncated = truncated,
        };
    }

    /// <summary>Drops the extra row the list query fetches past <paramref name="max"/>, reporting whether there was one.</summary>
    private static bool Truncate<T>(List<T> items, int max)
    {
        if (items.Count <= max)
        {
            return false;
        }

        items.RemoveRange(max, items.Count - max);
        return true;
    }

    /// <summary>The <c>Nullable(Float64)</c> <c>Figure</c> column (ordinal 2) - same null/non-finite handling as <see cref="HostInventoryQueryService"/>'s.</summary>
    private static double? ReadFigure(ClickHouseDataReader reader)
    {
        if (reader.IsDBNull(2))
        {
            return null;
        }

        var value = reader.GetDouble(2);
        return double.IsFinite(value) ? value : null;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
