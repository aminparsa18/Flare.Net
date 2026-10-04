using ClickHouse.Driver;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IRuntimeHealthQueryService
{
    Task<RuntimeHealthResponse> GetFindingsAsync(string service, RuntimeHealthRequest request, CancellationToken cancellationToken);
}

/// <summary>The one component holding an <see cref="IClickHouseClient"/> for runtime health - SQL in <see cref="RuntimeHealthQueryBuilder"/>, rules in <see cref="RuntimeHealthDetector"/>.</summary>
public sealed class RuntimeHealthQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IRuntimeHealthQueryService
{
    public async Task<RuntimeHealthResponse> GetFindingsAsync(string service, RuntimeHealthRequest request, CancellationToken cancellationToken)
    {
        var windowMinutes = RuntimeHealthQueryBuilder.ClampWindowMinutes(request.WindowMinutes);
        var bucketWidth = RuntimeHealthQueryBuilder.BucketWidthSecondsFor(windowMinutes);
        var end = HostInventoryQueryBuilder.ResolveWindowEnd(request.EndUnixMs, timeProvider.GetUtcNow());

        var built = RuntimeHealthQueryBuilder.Build(service, windowMinutes, bucketWidth, end);
        var points = new List<RuntimeMetricPoint>();
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
        while (reader.Read())
        {
            points.Add(new RuntimeMetricPoint(
                new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(0), DateTimeKind.Utc)),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetDouble(3)));
        }

        return new RuntimeHealthResponse
        {
            WindowMinutes = windowMinutes,
            HasRuntimeMetrics = points.Count > 0,
            Findings = RuntimeHealthDetector.Detect(points, end - TimeSpan.FromMinutes(windowMinutes), end, bucketWidth),
        };
    }
}
