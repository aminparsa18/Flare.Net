using ClickHouse.Driver;
using Flare.Api.Model;

namespace Flare.Api.Query;

public interface IServiceOverviewQueryService
{
    Task<ServiceOverviewResponse> GetOverviewAsync(int requestedWindowMinutes, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for the Services landing
/// page's RED-metrics rollup - same single-seam-per-page role as
/// <see cref="SpanQueryService"/>/<see cref="MetricQueryService"/>. Deliberately its own
/// sibling class rather than folded into <see cref="SpanQueryService"/>: this is one
/// unfiltered aggregate query with no cursor/pagination, not a search-shaped seam, so
/// sharing would mean threading a mismatched shape through a common interface for no
/// real reuse.
/// </summary>
public sealed class ServiceOverviewQueryService(IClickHouseClient client, TimeProvider timeProvider) : IServiceOverviewQueryService
{
    /// <summary>Converts a ClickHouse <c>DurationNano</c> quantile (nanoseconds) to milliseconds for the DTO.</summary>
    private const double NanosPerMilli = 1_000_000.0;

    public async Task<ServiceOverviewResponse> GetOverviewAsync(int requestedWindowMinutes, CancellationToken cancellationToken)
    {
        var windowMinutes = ServiceOverviewQueryBuilder.ClampWindowMinutes(requestedWindowMinutes);
        var window = TimeSpan.FromMinutes(windowMinutes);
        var now = timeProvider.GetUtcNow();

        var built = ServiceOverviewQueryBuilder.Build(window, now);

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var services = new List<ServiceMetrics>();
        while (reader.Read())
        {
            services.Add(BuildMetrics(
                serviceName: reader.GetString(0),
                requestCount: reader.GetFieldValue<ulong>(1),
                errorCount: reader.GetFieldValue<ulong>(2),
                p50DurationNano: reader.GetDouble(3),
                p95DurationNano: reader.GetDouble(4),
                p99DurationNano: reader.GetDouble(5),
                window: window));
        }

        return new ServiceOverviewResponse { WindowMinutes = windowMinutes, Services = services };
    }

    /// <summary>
    /// One ClickHouse row -&gt; <see cref="ServiceMetrics"/>, with no <see cref="IClickHouseClient"/>
    /// dependency - split out purely so the derived-stat math (error rate, requests/sec,
    /// the nanosecond-&gt;millisecond conversion) is unit-testable directly, same "test the
    /// pure function" precedent as <c>HistogramQuantileEstimator</c>/
    /// <c>IngestionStatsQueryService.BuildBuckets</c>. <c>internal</c>, not <c>private</c> -
    /// see <c>Flare.Api.csproj</c>'s <c>InternalsVisibleTo</c> for
    /// <c>Flare.Api.Tests</c>.
    /// </summary>
    internal static ServiceMetrics BuildMetrics(
        string serviceName,
        ulong requestCount,
        ulong errorCount,
        double p50DurationNano,
        double p95DurationNano,
        double p99DurationNano,
        TimeSpan window) =>
        new()
        {
            ServiceName = serviceName,
            RequestCount = requestCount,
            ErrorCount = errorCount,
            // requestCount > 0 always holds for a row this method is actually called
            // with - a service can only appear in the GROUP BY result at all if it had
            // at least one matching row (see ServiceOverviewQueryBuilder's remarks) - so
            // this is never a real 0/0 guard, just integer-vs-double division safety.
            ErrorRate = requestCount == 0 ? 0.0 : errorCount / (double)requestCount,
            RequestsPerSecond = requestCount / window.TotalSeconds,
            P50DurationMs = p50DurationNano / NanosPerMilli,
            P95DurationMs = p95DurationNano / NanosPerMilli,
            P99DurationMs = p99DurationNano / NanosPerMilli,
        };

    /// <summary>Same scan/time safety cap as <see cref="SpanQueryService.SafetyOptions"/>.</summary>
    private static QueryOptions SafetyOptions() => new()
    {
        CustomSettings = new Dictionary<string, object>
        {
            ["max_execution_time"] = 30,
            ["timeout_before_checking_execution_speed"] = 0,
            ["max_rows_to_read"] = 1_000_000_000,
            ["max_result_rows"] = 10_000,
            ["result_overflow_mode"] = "break",
        },
    };
}
