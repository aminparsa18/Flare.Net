using ClickHouse.Driver;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface ITraceFunnelQueryService
{
    Task<TraceFunnelResponse> GetFunnelAsync(TraceFunnelRequest request, CancellationToken cancellationToken);

    Task<TraceFunnelTracesResponse> GetTracesAsync(TraceFunnelTracesRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for trace funnels - see
/// <see cref="TraceFunnelQueryBuilder"/> for the SQL and semantics. Same
/// <c>ExecuteReaderAsync</c> + ordinal-read style as <see cref="MessagingQueryService"/>.
/// </summary>
public sealed class TraceFunnelQueryService(
    IClickHouseClient client,
    IOptions<QueryLimitsOptions> queryLimits,
    TimeProvider timeProvider,
    IPromotedAttributeRegistry promotedAttributes) : ITraceFunnelQueryService
{
    /// <summary>Columns <see cref="TraceFunnelQueryBuilder.BuildSummary"/> emits per step.</summary>
    private const int ColumnsPerStep = 4;

    public async Task<TraceFunnelResponse> GetFunnelAsync(TraceFunnelRequest request, CancellationToken cancellationToken)
    {
        var built = TraceFunnelQueryBuilder.BuildSummary(request, timeProvider.GetUtcNow(), promotedAttributes.Spans);
        var stepCount = request.Steps!.Count;

        var steps = new List<TraceFunnelStepResult>(stepCount);
        await using (var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken))
        {
            if (reader.Read())
            {
                for (var i = 0; i < stepCount; i++)
                {
                    var column = i * ColumnsPerStep;
                    var quantiles = reader.GetFieldValue<double[]>(column + 3);
                    steps.Add(new TraceFunnelStepResult
                    {
                        TraceCount = reader.GetFieldValue<ulong>(column),
                        ErrorCount = reader.GetFieldValue<ulong>(column + 1),
                        AvgTransitionMs = Finite(reader.GetDouble(column + 2)),
                        P50TransitionMs = Finite(quantiles.ElementAtOrDefault(0)),
                        P95TransitionMs = Finite(quantiles.ElementAtOrDefault(1)),
                        P99TransitionMs = Finite(quantiles.ElementAtOrDefault(2)),
                    });
                }
            }
        }

        return new TraceFunnelResponse
        {
            WindowMinutes = TraceFunnelQueryBuilder.ClampWindowMinutes(request.WindowMinutes),
            Steps = steps,
        };
    }

    public async Task<TraceFunnelTracesResponse> GetTracesAsync(TraceFunnelTracesRequest request, CancellationToken cancellationToken)
    {
        var built = TraceFunnelQueryBuilder.BuildTraces(request, timeProvider.GetUtcNow(), promotedAttributes.Spans);

        var traces = new List<TraceFunnelTrace>();
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
        while (reader.Read())
        {
            traces.Add(new TraceFunnelTrace
            {
                TraceId = reader.GetString(0),
                StartUnixMs = reader.GetFieldValue<long>(1),
                ReachedSteps = reader.GetFieldValue<int>(2),
                ElapsedMs = Finite(reader.GetDouble(3)),
            });
        }

        return new TraceFunnelTracesResponse { Traces = traces };
    }

    /// <summary>ClickHouse's <c>avgIf</c>/<c>quantilesIf</c> return <c>nan</c> over zero rows - a step nobody reached reads as 0.</summary>
    private static double Finite(double value) => double.IsFinite(value) ? value : 0;
}
