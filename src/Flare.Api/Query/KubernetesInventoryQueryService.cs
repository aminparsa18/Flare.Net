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

    Task<KubernetesWorkloadListResponse> ListWorkloadsAsync(KubernetesWorkloadKind kind, KubernetesWorkloadListRequest request, CancellationToken cancellationToken);

    Task<KubernetesWorkloadMetricsResponse> GetWorkloadMetricsAsync(KubernetesWorkloadKind kind, KubernetesWorkloadMetricsRequest request, CancellationToken cancellationToken);

    Task<KubernetesNamespaceListResponse> ListNamespacesAsync(KubernetesNamespaceListRequest request, CancellationToken cancellationToken);

    Task<KubernetesVolumeListResponse> ListVolumesAsync(KubernetesVolumeListRequest request, CancellationToken cancellationToken);

    Task<KubernetesVolumeMetricsResponse> GetVolumeMetricsAsync(KubernetesVolumeMetricsRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for the Kubernetes page - see
/// <see cref="KubernetesInventoryQueryBuilder"/> (Nodes/Pods) and
/// <see cref="KubernetesWorkloadQueryBuilder"/> (Workloads/Namespaces/Volumes) for the SQL. Same
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

    public async Task<KubernetesWorkloadListResponse> ListWorkloadsAsync(KubernetesWorkloadKind kind, KubernetesWorkloadListRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = HostInventoryQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var now = timeProvider.GetUtcNow();

        var listSql = KubernetesWorkloadQueryBuilder.BuildWorkloadList(kind, request, windowMinutes, now);
        var workloads = new List<KubernetesWorkloadSummary>();
        await using (var reader = await client.ExecuteReaderAsync(listSql.Sql, listSql.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                workloads.Add(new KubernetesWorkloadSummary
                {
                    Namespace = reader.GetString(0),
                    Name = reader.GetString(1),
                    LastSeen = ReadUtc(reader, 2),
                });
            }
        }

        var truncated = Truncate(workloads, KubernetesWorkloadQueryBuilder.MaxWorkloads);
        if (workloads.Count > 0)
        {
            static string KeyOf(KubernetesWorkloadSummary w) => KubernetesWorkloadQueryBuilder.WorkloadKey(w.Namespace, w.Name);

            var valuesSql = KubernetesWorkloadQueryBuilder.BuildWorkloadValues(
                kind, workloads.ConvertAll(KeyOf), windowMinutes, HostInventoryQueryBuilder.BucketWidthSecondsFor(windowMinutes), now);
            var byKey = workloads.ToDictionary(KeyOf, StringComparer.Ordinal);
            await using (var reader = await client.ExecuteReaderAsync(valuesSql.Sql, valuesSql.Parameters, SafetyOptions(), cancellationToken))
            {
                while (reader.Read())
                {
                    var key = reader.GetString(0);
                    if (byKey.TryGetValue(key, out var summary) && ReadFigure(reader) is { } value)
                    {
                        byKey[key] = ApplyWorkloadFigure(summary, reader.GetString(1), value);
                    }
                }
            }

            workloads = workloads.ConvertAll(w => byKey[KeyOf(w)]);
        }

        return new KubernetesWorkloadListResponse { Kind = kind.Kind, WindowMinutes = windowMinutes, Workloads = workloads, Truncated = truncated };
    }

    public async Task<KubernetesWorkloadMetricsResponse> GetWorkloadMetricsAsync(KubernetesWorkloadKind kind, KubernetesWorkloadMetricsRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = HostInventoryQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var bucketWidthSeconds = HostInventoryQueryBuilder.BucketWidthSecondsFor(windowMinutes);
        var built = KubernetesWorkloadQueryBuilder.BuildWorkloadMetrics(kind, request.Namespace, request.Name, windowMinutes, bucketWidthSeconds, timeProvider.GetUtcNow());

        var byBucket = new SortedDictionary<DateTimeOffset, KubernetesWorkloadMetricsPoint>();
        await using (var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                if (ReadFigure(reader) is not { } value)
                {
                    continue;
                }

                var bucket = ReadUtc(reader, 0);
                var point = byBucket.GetValueOrDefault(bucket) ?? new KubernetesWorkloadMetricsPoint { BucketStart = bucket };
                var kindName = reader.GetString(1);
                byBucket[bucket] = kindName switch
                {
                    KubernetesWorkloadQueryBuilder.DesiredKind => point with { Desired = (int)value },
                    KubernetesWorkloadQueryBuilder.ReadyKind => point with { Ready = (int)value },
                    KubernetesWorkloadQueryBuilder.CurrentKind => point with { Current = (int)value },
                    KubernetesWorkloadQueryBuilder.ActiveKind => point with { Active = (int)value },
                    KubernetesWorkloadQueryBuilder.SucceededKind => point with { Succeeded = (int)value },
                    KubernetesWorkloadQueryBuilder.FailedKind => point with { Failed = (int)value },
                    KubernetesWorkloadQueryBuilder.CpuKind => point with { CpuCores = value },
                    KubernetesWorkloadQueryBuilder.MemoryKind => point with { MemoryWorkingSetBytes = value },
                    _ => point,
                };
            }
        }

        return new KubernetesWorkloadMetricsResponse
        {
            Kind = kind.Kind,
            Namespace = request.Namespace,
            Name = request.Name,
            WindowMinutes = windowMinutes,
            BucketWidthSeconds = bucketWidthSeconds,
            Points = [.. byBucket.Values],
        };
    }

    public async Task<KubernetesNamespaceListResponse> ListNamespacesAsync(KubernetesNamespaceListRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = HostInventoryQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var now = timeProvider.GetUtcNow();

        var listSql = KubernetesWorkloadQueryBuilder.BuildNamespaceList(request, windowMinutes, now);
        var namespaces = new List<KubernetesNamespaceSummary>();
        await using (var reader = await client.ExecuteReaderAsync(listSql.Sql, listSql.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                namespaces.Add(new KubernetesNamespaceSummary { Namespace = reader.GetString(0), LastSeen = ReadUtc(reader, 1) });
            }
        }

        var truncated = Truncate(namespaces, KubernetesWorkloadQueryBuilder.MaxNamespaces);
        if (namespaces.Count > 0)
        {
            var valuesSql = KubernetesWorkloadQueryBuilder.BuildNamespaceValues(
                namespaces.ConvertAll(n => n.Namespace), windowMinutes, HostInventoryQueryBuilder.BucketWidthSecondsFor(windowMinutes), now);
            var byName = namespaces.ToDictionary(n => n.Namespace, StringComparer.Ordinal);
            await using (var reader = await client.ExecuteReaderAsync(valuesSql.Sql, valuesSql.Parameters, SafetyOptions(), cancellationToken))
            {
                while (reader.Read())
                {
                    var name = reader.GetString(0);
                    if (byName.TryGetValue(name, out var summary) && ReadFigure(reader) is { } value)
                    {
                        byName[name] = reader.GetString(1) switch
                        {
                            KubernetesWorkloadQueryBuilder.NamespacePhaseKind => summary with { Phase = KubernetesWorkloadQueryBuilder.DecodeNamespacePhase(value) },
                            KubernetesWorkloadQueryBuilder.PodCountKind => summary with { PodCount = (int)value },
                            KubernetesWorkloadQueryBuilder.CpuKind => summary with { CpuCores = value },
                            KubernetesWorkloadQueryBuilder.MemoryKind => summary with { MemoryWorkingSetBytes = value },
                            _ => summary,
                        };
                    }
                }
            }

            namespaces = namespaces.ConvertAll(n => byName[n.Namespace]);
        }

        return new KubernetesNamespaceListResponse { WindowMinutes = windowMinutes, Namespaces = namespaces, Truncated = truncated };
    }

    public async Task<KubernetesVolumeListResponse> ListVolumesAsync(KubernetesVolumeListRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = HostInventoryQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var now = timeProvider.GetUtcNow();

        var listSql = KubernetesWorkloadQueryBuilder.BuildVolumeList(request, windowMinutes, now);
        var volumes = new List<KubernetesVolumeSummary>();
        await using (var reader = await client.ExecuteReaderAsync(listSql.Sql, listSql.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                volumes.Add(new KubernetesVolumeSummary
                {
                    Namespace = reader.GetString(0),
                    PodName = reader.GetString(1),
                    VolumeName = reader.GetString(2),
                    VolumeType = NullIfEmpty(reader.GetString(3)),
                    ClaimName = NullIfEmpty(reader.GetString(4)),
                    LastSeen = ReadUtc(reader, 5),
                });
            }
        }

        var truncated = Truncate(volumes, KubernetesWorkloadQueryBuilder.MaxVolumes);
        if (volumes.Count > 0)
        {
            static string KeyOf(KubernetesVolumeSummary v) => KubernetesWorkloadQueryBuilder.VolumeKey(v.Namespace, v.PodName, v.VolumeName);

            var valuesSql = KubernetesWorkloadQueryBuilder.BuildVolumeValues(volumes.ConvertAll(KeyOf), windowMinutes, now);
            var figures = await ReadVolumeFiguresAsync(valuesSql, reader => reader.GetString(0), cancellationToken);
            volumes = volumes.ConvertAll(v => figures.TryGetValue(KeyOf(v), out var f)
                ? v with
                {
                    CapacityBytes = f.Capacity,
                    AvailableBytes = f.Available,
                    UsedBytes = KubernetesWorkloadQueryBuilder.UsedBytes(f.Capacity, f.Available),
                    UsedPercent = KubernetesWorkloadQueryBuilder.UsedPercent(f.Capacity, f.Available),
                    InodesUsedPercent = KubernetesWorkloadQueryBuilder.InodesUsedPercent(f.Inodes, f.InodesUsed, f.InodesFree),
                }
                : v);
        }

        return new KubernetesVolumeListResponse { WindowMinutes = windowMinutes, Volumes = volumes, Truncated = truncated };
    }

    public async Task<KubernetesVolumeMetricsResponse> GetVolumeMetricsAsync(KubernetesVolumeMetricsRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = HostInventoryQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var bucketWidthSeconds = HostInventoryQueryBuilder.BucketWidthSecondsFor(windowMinutes);
        var built = KubernetesWorkloadQueryBuilder.BuildVolumeMetrics(
            request.Namespace, request.PodName, request.VolumeName, windowMinutes, bucketWidthSeconds, timeProvider.GetUtcNow());

        var figures = await ReadVolumeFiguresAsync(built, reader => ReadUtc(reader, 0), cancellationToken);

        return new KubernetesVolumeMetricsResponse
        {
            Namespace = request.Namespace,
            PodName = request.PodName,
            VolumeName = request.VolumeName,
            WindowMinutes = windowMinutes,
            BucketWidthSeconds = bucketWidthSeconds,
            Points =
            [
                .. figures
                    .Select(pair => new KubernetesVolumeMetricsPoint
                    {
                        BucketStart = pair.Key,
                        UsedBytes = KubernetesWorkloadQueryBuilder.UsedBytes(pair.Value.Capacity, pair.Value.Available),
                        UsedPercent = KubernetesWorkloadQueryBuilder.UsedPercent(pair.Value.Capacity, pair.Value.Available),
                        InodesUsedPercent = KubernetesWorkloadQueryBuilder.InodesUsedPercent(pair.Value.Inodes, pair.Value.InodesUsed, pair.Value.InodesFree),
                    })
                    .OrderBy(p => p.BucketStart),
            ],
        };
    }

    private sealed record VolumeFigures(double? Capacity, double? Available, double? Inodes, double? InodesUsed, double? InodesFree);

    /// <summary>A volume values/metrics statement's rows folded per <paramref name="keyOf"/> - a volume key for the list, a bucket start for the drill-down.</summary>
    private async Task<Dictionary<TKey, VolumeFigures>> ReadVolumeFiguresAsync<TKey>(
        HostInventorySql sql, Func<ClickHouseDataReader, TKey> keyOf, CancellationToken cancellationToken)
        where TKey : notnull
    {
        var byKey = new Dictionary<TKey, VolumeFigures>();
        await using var reader = await client.ExecuteReaderAsync(sql.Sql, sql.Parameters, SafetyOptions(), cancellationToken);
        while (reader.Read())
        {
            if (ReadFigure(reader) is not { } value)
            {
                continue;
            }

            var key = keyOf(reader);
            var figures = byKey.GetValueOrDefault(key) ?? new VolumeFigures(null, null, null, null, null);
            byKey[key] = reader.GetString(1) switch
            {
                KubernetesWorkloadQueryBuilder.CapacityKind => figures with { Capacity = value },
                KubernetesWorkloadQueryBuilder.AvailableKind => figures with { Available = value },
                KubernetesWorkloadQueryBuilder.InodesKind => figures with { Inodes = value },
                KubernetesWorkloadQueryBuilder.InodesUsedKind => figures with { InodesUsed = value },
                KubernetesWorkloadQueryBuilder.InodesFreeKind => figures with { InodesFree = value },
                _ => figures,
            };
        }

        return byKey;
    }

    private static KubernetesWorkloadSummary ApplyWorkloadFigure(KubernetesWorkloadSummary summary, string kind, double value) => kind switch
    {
        KubernetesWorkloadQueryBuilder.DesiredKind => summary with { Desired = (int)value },
        KubernetesWorkloadQueryBuilder.ReadyKind => summary with { Ready = (int)value },
        KubernetesWorkloadQueryBuilder.CurrentKind => summary with { Current = (int)value },
        KubernetesWorkloadQueryBuilder.ActiveKind => summary with { Active = (int)value },
        KubernetesWorkloadQueryBuilder.SucceededKind => summary with { Succeeded = (int)value },
        KubernetesWorkloadQueryBuilder.FailedKind => summary with { Failed = (int)value },
        KubernetesWorkloadQueryBuilder.PodCountKind => summary with { PodCount = (int)value },
        KubernetesWorkloadQueryBuilder.CpuKind => summary with { CpuCores = value },
        KubernetesWorkloadQueryBuilder.MemoryKind => summary with { MemoryWorkingSetBytes = value },
        _ => summary,
    };

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
