using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Readers;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IHostInventoryQueryService
{
    Task<HostListResponse> ListAsync(HostListRequest request, CancellationToken cancellationToken);

    Task<HostMetricsResponse> GetMetricsAsync(HostMetricsRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for the Hosts page - see
/// <see cref="HostInventoryQueryBuilder"/> for the SQL and which metric feeds which column.
/// Same <c>ExecuteReaderAsync</c> + ordinal-read style as <see cref="MetricQueryService"/>.
/// </summary>
public sealed class HostInventoryQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IHostInventoryQueryService
{
    public async Task<HostListResponse> ListAsync(HostListRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = HostInventoryQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var now = timeProvider.GetUtcNow();

        var hostsSql = HostInventoryQueryBuilder.BuildHostList(request, windowMinutes, now);
        var hosts = new List<HostSummary>();
        await using (var reader = await client.ExecuteReaderAsync(hostsSql.Sql, hostsSql.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                hosts.Add(new HostSummary
                {
                    HostName = reader.GetString(0),
                    OsType = NullIfEmpty(reader.GetString(1)),
                    LastSeen = ReadUtc(reader, 2),
                });
            }
        }

        var truncated = hosts.Count > HostInventoryQueryBuilder.MaxHosts;
        if (truncated)
        {
            hosts.RemoveRange(HostInventoryQueryBuilder.MaxHosts, hosts.Count - HostInventoryQueryBuilder.MaxHosts);
        }

        if (hosts.Count == 0)
        {
            return new HostListResponse { WindowMinutes = windowMinutes, Hosts = hosts, Truncated = false };
        }

        var valuesSql = HostInventoryQueryBuilder.BuildListValues(hosts.ConvertAll(h => h.HostName), windowMinutes, now);
        var byHost = hosts.ToDictionary(h => h.HostName, StringComparer.Ordinal);
        await using (var reader = await client.ExecuteReaderAsync(valuesSql.Sql, valuesSql.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                var host = reader.GetString(0);
                if (byHost.TryGetValue(host, out var summary) && ReadValue(reader) is { } value)
                {
                    byHost[host] = reader.GetString(1) switch
                    {
                        HostInventoryQueryBuilder.CpuKind => summary with { CpuPercent = value },
                        HostInventoryQueryBuilder.MemoryKind => summary with { MemoryPercent = value },
                        HostInventoryQueryBuilder.DiskKind => summary with { DiskPercent = value },
                        HostInventoryQueryBuilder.LoadKind => summary with { LoadAverage15m = value },
                        _ => summary,
                    };
                }
            }
        }

        return new HostListResponse
        {
            WindowMinutes = windowMinutes,
            Hosts = hosts.ConvertAll(h => byHost[h.HostName]),
            Truncated = truncated,
        };
    }

    public async Task<HostMetricsResponse> GetMetricsAsync(HostMetricsRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = HostInventoryQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var bucketWidthSeconds = HostInventoryQueryBuilder.BucketWidthSecondsFor(windowMinutes);
        var built = HostInventoryQueryBuilder.BuildHostMetrics(request.HostName, windowMinutes, bucketWidthSeconds, timeProvider.GetUtcNow());

        var byBucket = new SortedDictionary<DateTimeOffset, HostMetricsPoint>();
        await using (var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                if (ReadValue(reader) is not { } value)
                {
                    continue;
                }

                var bucket = ReadUtc(reader, 0);
                var point = byBucket.GetValueOrDefault(bucket) ?? new HostMetricsPoint { BucketStart = bucket };
                byBucket[bucket] = reader.GetString(1) switch
                {
                    HostInventoryQueryBuilder.CpuKind => point with { CpuPercent = value },
                    HostInventoryQueryBuilder.MemoryKind => point with { MemoryPercent = value },
                    HostInventoryQueryBuilder.DiskKind => point with { DiskPercent = value },
                    HostInventoryQueryBuilder.LoadKind => point with { LoadAverage15m = value },
                    _ => point,
                };
            }
        }

        return new HostMetricsResponse
        {
            HostName = request.HostName,
            WindowMinutes = windowMinutes,
            BucketWidthSeconds = bucketWidthSeconds,
            Points = [.. byBucket.Values],
        };
    }

    /// <summary>The <c>Nullable(Float64)</c> <c>Value</c> column (ordinal 2) - null for a division by a zero total, and non-finite values are treated the same.</summary>
    private static double? ReadValue(ClickHouseDataReader reader)
    {
        if (reader.IsDBNull(2))
        {
            return null;
        }

        var value = reader.GetDouble(2);
        return double.IsFinite(value) ? value : null;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>Same UTC re-tagging rationale as <see cref="LogQueryService"/>'s own <c>ReadUtc</c>.</summary>
    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
