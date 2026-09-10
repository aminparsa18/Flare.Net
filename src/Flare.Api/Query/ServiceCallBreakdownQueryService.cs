using ClickHouse.Driver;
using Flare.Api.Model;

namespace Flare.Api.Query;

public interface IServiceCallBreakdownQueryService
{
    Task<ServiceCallBreakdownResponse> GetBreakdownAsync(string service, int requestedWindowMinutes, IReadOnlyList<ResourceAttributeFilter>? resourceAttributes, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for the Services tab Map
/// view's per-node drill-down - same single-seam-per-view role as
/// <see cref="ServiceDependencyQueryService"/>/<see cref="ServiceOverviewQueryService"/>.
/// See <see cref="ServiceCallBreakdownQueryBuilder"/>'s remarks for the query design.
/// </summary>
public sealed class ServiceCallBreakdownQueryService(IClickHouseClient client, TimeProvider timeProvider) : IServiceCallBreakdownQueryService
{
    private const double NanosPerMilli = 1_000_000.0;

    public async Task<ServiceCallBreakdownResponse> GetBreakdownAsync(string service, int requestedWindowMinutes, IReadOnlyList<ResourceAttributeFilter>? resourceAttributes, CancellationToken cancellationToken)
    {
        var windowMinutes = ServiceCallBreakdownQueryBuilder.ClampWindowMinutes(requestedWindowMinutes);
        var window = TimeSpan.FromMinutes(windowMinutes);
        var now = timeProvider.GetUtcNow();

        var built = ServiceCallBreakdownQueryBuilder.Build(service, window, now, resourceAttributes);

        var externalCalls = new List<ExternalCallGroup>();
        await using (var reader = await client.ExecuteReaderAsync(built.ExternalCallsSql, built.ExternalCallsParameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                var callCount = reader.GetFieldValue<ulong>(1);
                var errorCount = reader.GetFieldValue<ulong>(2);
                externalCalls.Add(new ExternalCallGroup
                {
                    PeerService = reader.GetString(0),
                    CallCount = callCount,
                    ErrorCount = errorCount,
                    ErrorRate = ErrorRate(errorCount, callCount),
                    P50DurationMs = reader.GetDouble(3) / NanosPerMilli,
                    P95DurationMs = reader.GetDouble(4) / NanosPerMilli,
                });
            }
        }

        var databaseCalls = new List<DatabaseCallGroup>();
        await using (var reader = await client.ExecuteReaderAsync(built.DatabaseCallsSql, built.DatabaseCallsParameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                var callCount = reader.GetFieldValue<ulong>(2);
                var errorCount = reader.GetFieldValue<ulong>(3);
                databaseCalls.Add(new DatabaseCallGroup
                {
                    DbSystem = reader.GetString(0),
                    DbOperation = reader.GetString(1),
                    CallCount = callCount,
                    ErrorCount = errorCount,
                    ErrorRate = ErrorRate(errorCount, callCount),
                    P50DurationMs = reader.GetDouble(4) / NanosPerMilli,
                    P95DurationMs = reader.GetDouble(5) / NanosPerMilli,
                });
            }
        }

        return new ServiceCallBreakdownResponse
        {
            Service = service,
            WindowMinutes = windowMinutes,
            ExternalCalls = externalCalls,
            DatabaseCalls = databaseCalls,
        };
    }

    /// <summary>
    /// <c>errorCount / callCount</c>, 0.0-1.0 - split out as its own pure, ClickHouse-free
    /// method (rather than inlined at each of the two call sites above) so the shared
    /// "0/0 division safety, never a real 0/0 in practice" reasoning
    /// <see cref="ServiceOverviewQueryService.BuildMetrics"/> documents is unit-testable
    /// directly here too, without needing a fake <see cref="IClickHouseClient"/>.
    /// </summary>
    internal static double ErrorRate(ulong errorCount, ulong callCount) =>
        callCount == 0 ? 0.0 : errorCount / (double)callCount;

    /// <summary>Same scan/time safety cap as <see cref="ServiceOverviewQueryService.SafetyOptions"/> - see <see cref="ServiceCallBreakdownQueryBuilder"/>'s remarks on why this pair of queries is cheaper than <see cref="ServiceDependencyQueryService"/>'s.</summary>
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
