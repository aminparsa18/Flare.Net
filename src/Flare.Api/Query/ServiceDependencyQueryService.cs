using ClickHouse.Driver;
using Flare.Api.Model;

namespace Flare.Api.Query;

public interface IServiceDependencyQueryService
{
    Task<ServiceDependencyGraphResponse> GetGraphAsync(int requestedWindowMinutes, IReadOnlyList<ResourceAttributeFilter>? resourceAttributes, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for the Services tab's
/// "Map" view - same single-seam-per-view role as <see cref="ServiceOverviewQueryService"/>
/// (its Table-view sibling). Deliberately its own class rather than folded into
/// <see cref="ServiceOverviewQueryService"/>: two queries with a genuinely different shape
/// (a self-join producing edges, not just a <c>GROUP BY</c>), sharing only the window
/// clamp - see <see cref="ServiceDependencyQueryBuilder"/>'s remarks for the query design.
/// </summary>
public sealed class ServiceDependencyQueryService(IClickHouseClient client, TimeProvider timeProvider) : IServiceDependencyQueryService
{
    public async Task<ServiceDependencyGraphResponse> GetGraphAsync(int requestedWindowMinutes, IReadOnlyList<ResourceAttributeFilter>? resourceAttributes, CancellationToken cancellationToken)
    {
        var windowMinutes = ServiceDependencyQueryBuilder.ClampWindowMinutes(requestedWindowMinutes);
        var window = TimeSpan.FromMinutes(windowMinutes);
        var now = timeProvider.GetUtcNow();

        var built = ServiceDependencyQueryBuilder.Build(window, now, resourceAttributes);

        var nodes = new List<ServiceDependencyNode>();
        await using (var reader = await client.ExecuteReaderAsync(built.NodesSql, built.NodesParameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                nodes.Add(new ServiceDependencyNode
                {
                    Service = reader.GetString(0),
                    SpanCount = reader.GetFieldValue<ulong>(1),
                    ErrorCount = reader.GetFieldValue<ulong>(2),
                    TotalDurationNano = reader.GetFieldValue<ulong>(3),
                    TopOperations = reader.GetFieldValue<string[]>(4),
                });
            }
        }

        var edges = new List<ServiceDependencyEdge>();
        await using (var reader = await client.ExecuteReaderAsync(built.EdgesSql, built.EdgesParameters, SafetyOptions(), cancellationToken))
        {
            while (reader.Read())
            {
                edges.Add(new ServiceDependencyEdge
                {
                    Source = reader.GetString(0),
                    Target = reader.GetString(1),
                    CallCount = reader.GetFieldValue<ulong>(2),
                    TotalDurationNano = reader.GetFieldValue<ulong>(3),
                });
            }
        }

        return new ServiceDependencyGraphResponse { WindowMinutes = windowMinutes, Nodes = nodes, Edges = edges };
    }

    /// <summary>Same scan/time safety cap as <see cref="ServiceOverviewQueryService.SafetyOptions"/> - see <see cref="ServiceDependencyQueryBuilder"/>'s remarks on why the edges query in particular has no index to lean on.</summary>
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
