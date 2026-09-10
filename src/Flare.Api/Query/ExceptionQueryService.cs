using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Readers;
using Flare.Api.Model;

namespace Flare.Api.Query;

public interface IExceptionQueryService
{
    Task<ExceptionGroupsResponse> GetGroupsAsync(ExceptionGroupsRequest request, CancellationToken cancellationToken);

    Task<ExceptionOccurrencesResponse> GetOccurrencesAsync(ExceptionOccurrencesRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for the <c>/errors</c> page -
/// same role/style as <see cref="SpanQueryService"/>/<see cref="ServiceOverviewQueryService"/>
/// (manual <c>ExecuteReaderAsync</c> + ordinal reads, not <c>QueryAsync&lt;T&gt;</c> - see
/// <see cref="LogQueryService"/>'s remarks for why), deliberately its own sibling class rather
/// than folded into <see cref="SpanQueryService"/> - the read shapes (an <c>ARRAY JOIN</c>'d
/// aggregate vs. a whole-span search) diverge enough to cost more shared-code complexity than
/// they'd save.
/// </summary>
public sealed class ExceptionQueryService(IClickHouseClient client, TimeProvider timeProvider) : IExceptionQueryService
{
    public async Task<ExceptionGroupsResponse> GetGroupsAsync(ExceptionGroupsRequest request, CancellationToken cancellationToken)
    {
        var built = ExceptionGroupQueryBuilder.Build(request, timeProvider.GetUtcNow());

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var groups = new List<ExceptionGroup>();
        while (reader.Read())
        {
            groups.Add(new ExceptionGroup
            {
                ExceptionType = reader.GetString(0),
                ExceptionMessage = reader.GetString(1),
                OccurrenceCount = reader.GetFieldValue<ulong>(2),
                FirstSeen = ReadUtc(reader, 3),
                LastSeen = ReadUtc(reader, 4),
                AffectedServices = reader.GetFieldValue<string[]>(5),
            });
        }

        return new ExceptionGroupsResponse { Groups = groups };
    }

    public async Task<ExceptionOccurrencesResponse> GetOccurrencesAsync(ExceptionOccurrencesRequest request, CancellationToken cancellationToken)
    {
        var built = ExceptionOccurrenceQueryBuilder.Build(request, timeProvider.GetUtcNow());

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var occurrences = new List<ExceptionOccurrence>();
        while (reader.Read())
        {
            occurrences.Add(new ExceptionOccurrence
            {
                TraceId = reader.GetString(0),
                SpanId = reader.GetString(1),
                ServiceName = reader.GetString(2),
                SpanName = reader.GetString(3),
                Timestamp = ReadUtc(reader, 4),
                Stacktrace = reader.GetString(5),
            });
        }

        return new ExceptionOccurrencesResponse
        {
            ExceptionType = request.ExceptionType,
            ExceptionMessage = request.ExceptionMessage,
            Occurrences = occurrences,
        };
    }

    /// <summary>Same UTC re-tagging rationale as <see cref="LogQueryService"/>/<see cref="SpanQueryService"/>'s own <c>ReadUtc</c> - <c>Flare.Ingest</c> always writes UTC wall-clock values.</summary>
    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

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
