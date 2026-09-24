using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using Flare.Api.Model;
using Flare.Identity.Apdex;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IServiceOverviewQueryService
{
    Task<ServiceOverviewResponse> GetOverviewAsync(int requestedWindowMinutes, IReadOnlyList<ResourceAttributeFilter>? resourceAttributes, CancellationToken cancellationToken);
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
/// <remarks>
/// Reads from one of two tables depending on the request - see <see cref="GetOverviewAsync"/>.
/// Both <see cref="ServiceMetricsQueryBuilder"/> (pre-aggregated) and
/// <see cref="ServiceOverviewQueryBuilder"/> (live) produce the same 6-column result shape,
/// so <see cref="BuildMetrics"/> maps either one's reader unchanged.
/// <para>
/// Apdex (<see cref="ServiceApdexQueryBuilder"/>) is a separate, always-live query, run
/// regardless of which of the two tables above serves the RED metrics - see
/// docs-internal/adr/0032-apdex-score-per-service.md for why it can't share
/// <see cref="ServiceMetricsQueryBuilder"/>'s pre-aggregated path.
/// </para>
/// </remarks>
public sealed class ServiceOverviewQueryService(
    IClickHouseClient client,
    IOptions<QueryLimitsOptions> queryLimits,
    TimeProvider timeProvider,
    IOptions<ServiceMetricsOptions> serviceMetricsOptions,
    IApdexThresholdStore apdexThresholdStore) : IServiceOverviewQueryService
{
    /// <summary>Converts a ClickHouse <c>DurationNano</c> quantile (nanoseconds) to milliseconds for the DTO.</summary>
    private const double NanosPerMilli = 1_000_000.0;

    public async Task<ServiceOverviewResponse> GetOverviewAsync(int requestedWindowMinutes, IReadOnlyList<ResourceAttributeFilter>? resourceAttributes, CancellationToken cancellationToken)
    {
        var windowMinutes = ServiceOverviewQueryBuilder.ClampWindowMinutes(requestedWindowMinutes);
        var window = TimeSpan.FromMinutes(windowMinutes);
        var now = timeProvider.GetUtcNow();

        var thresholdOverrides = await apdexThresholdStore.GetAllAsync(cancellationToken);
        var apdexCounts = await GetApdexCountsAsync(window, now, thresholdOverrides, resourceAttributes, cancellationToken);

        // The pre-aggregated service_metrics table (ADR-0030) has no dimension for the
        // Services tab's arbitrary resource-attribute filter chips - only queried when
        // none are present. ServiceMetricsOptions.Enabled is the instant rollback valve:
        // false always takes the live path below, same as if a filter were always set.
        var useServiceMetrics = resourceAttributes is not { Count: > 0 } && serviceMetricsOptions.Value.Enabled;

        string sql;
        ClickHouseParameterCollection parameters;
        if (useServiceMetrics)
        {
            var built = ServiceMetricsQueryBuilder.Build(window, now);
            sql = built.Sql;
            parameters = built.Parameters;
        }
        else
        {
            var built = ServiceOverviewQueryBuilder.Build(window, now, resourceAttributes);
            sql = built.Sql;
            parameters = built.Parameters;
        }

        await using var reader = await client.ExecuteReaderAsync(sql, parameters, SafetyOptions(), cancellationToken);

        var services = new List<ServiceMetrics>();
        while (reader.Read())
        {
            var serviceName = reader.GetString(0);
            apdexCounts.TryGetValue(serviceName, out var apdex);
            services.Add(BuildMetrics(
                serviceName: serviceName,
                requestCount: reader.GetFieldValue<ulong>(1),
                errorCount: reader.GetFieldValue<ulong>(2),
                p50DurationNano: reader.GetDouble(3),
                p95DurationNano: reader.GetDouble(4),
                p99DurationNano: reader.GetDouble(5),
                window: window,
                apdexSatisfiedCount: apdex.Satisfied,
                apdexToleratingCount: apdex.Tolerating,
                apdexThresholdMs: thresholdOverrides.GetValueOrDefault(serviceName, ServiceApdexQueryBuilder.DefaultThresholdMs)));
        }

        return new ServiceOverviewResponse { WindowMinutes = windowMinutes, Services = services };
    }

    /// <summary>Runs <see cref="ServiceApdexQueryBuilder"/> and collects its rows into a
    /// per-service lookup. Always live - see this class's remarks.</summary>
    private async Task<Dictionary<string, (ulong Satisfied, ulong Tolerating)>> GetApdexCountsAsync(
        TimeSpan window,
        DateTimeOffset now,
        IReadOnlyDictionary<string, int> thresholdOverrides,
        IReadOnlyList<ResourceAttributeFilter>? resourceAttributes,
        CancellationToken cancellationToken)
    {
        var built = ServiceApdexQueryBuilder.Build(window, now, thresholdOverrides, resourceAttributes);
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var counts = new Dictionary<string, (ulong Satisfied, ulong Tolerating)>();
        while (reader.Read())
        {
            counts[reader.GetString(0)] = (reader.GetFieldValue<ulong>(1), reader.GetFieldValue<ulong>(2));
        }

        return counts;
    }

    /// <summary>
    /// One ClickHouse row -&gt; <see cref="ServiceMetrics"/>, with no <see cref="IClickHouseClient"/>
    /// dependency - split out purely so the derived-stat math (error rate, requests/sec,
    /// the nanosecond-&gt;millisecond conversion, Apdex) is unit-testable directly, same
    /// "test the pure function" precedent as <c>HistogramQuantileEstimator</c>/
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
        TimeSpan window,
        ulong apdexSatisfiedCount,
        ulong apdexToleratingCount,
        int apdexThresholdMs) =>
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
            ApdexScore = ApdexScoreCalculator.Calculate(apdexSatisfiedCount, apdexToleratingCount, requestCount),
            ApdexThresholdMs = apdexThresholdMs,
        };

    /// <summary>Same scan/time safety cap as <see cref="SpanQueryService.SafetyOptions"/>.</summary>
    private QueryOptions SafetyOptions() => QuerySafety.Full(queryLimits.Value);
}
