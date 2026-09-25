using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Readers;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IPodMetricsQueryService
{
    Task<PodMetricsResponse> GetMetricsAsync(PodMetricsRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for <c>POST /api/pods/metrics</c> -
/// see <see cref="PodMetricsQueryBuilder"/> for the SQL. Same shape as
/// <see cref="HostInventoryQueryService.GetMetricsAsync"/>, including its window clamp,
/// window-end and bucket-width rules.
/// </summary>
public sealed class PodMetricsQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IPodMetricsQueryService
{
    public async Task<PodMetricsResponse> GetMetricsAsync(PodMetricsRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = HostInventoryQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var bucketWidthSeconds = HostInventoryQueryBuilder.BucketWidthSecondsFor(windowMinutes);
        var end = HostInventoryQueryBuilder.ResolveWindowEnd(request.EndUnixMs, timeProvider.GetUtcNow());
        var built = PodMetricsQueryBuilder.BuildPodMetrics(request.PodName, request.Namespace, windowMinutes, bucketWidthSeconds, end);

        var byBucket = new SortedDictionary<DateTimeOffset, PodMetricsPoint>();
        await using (var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken))
        {
            while (reader.Read())
            {
                if (ReadValue(reader) is not { } value)
                {
                    continue;
                }

                var bucket = new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc));
                var point = byBucket.GetValueOrDefault(bucket) ?? new PodMetricsPoint { BucketStart = bucket };
                byBucket[bucket] = reader.GetString(1) switch
                {
                    PodMetricsQueryBuilder.CpuKind => point with { CpuCores = value },
                    PodMetricsQueryBuilder.MemoryKind => point with { MemoryWorkingSetBytes = value },
                    PodMetricsQueryBuilder.CpuLimitKind => point with { CpuLimitPercent = value },
                    PodMetricsQueryBuilder.MemoryLimitKind => point with { MemoryLimitPercent = value },
                    _ => point,
                };
            }
        }

        return new PodMetricsResponse
        {
            PodName = request.PodName,
            WindowMinutes = windowMinutes,
            BucketWidthSeconds = bucketWidthSeconds,
            Points = [.. byBucket.Values],
        };
    }

    /// <summary>Same null/non-finite handling as <see cref="HostInventoryQueryService"/>'s.</summary>
    private static double? ReadValue(ClickHouseDataReader reader)
    {
        if (reader.IsDBNull(2))
        {
            return null;
        }

        var value = reader.GetDouble(2);
        return double.IsFinite(value) ? value : null;
    }
}
