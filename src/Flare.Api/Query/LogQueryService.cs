using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Readers;
using Flare.Api.Model;

namespace Flare.Api.Query;

public interface ILogQueryService
{
    Task<LogSearchResponse> SearchAsync(LogSearchRequest request, CancellationToken cancellationToken);

    Task<LogAggregateResponse> AggregateAsync(LogAggregateRequest request, CancellationToken cancellationToken);

    /// <summary>Ranked Drain clusters ("log patterns") within the request's filter window - see <see cref="LogPatternQueryBuilder"/>.</summary>
    Task<LogPatternResponse> GetPatternsAsync(LogPatternRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Every distinct <c>ServiceName</c> that has logged at least one event within
    /// <paramref name="window"/> of now, with each one's most recent event timestamp -
    /// the data behind the Resources page's producer-services overlay (see
    /// <see cref="DockerResources.DockerContainerPoller"/>'s remarks). Not filtered
    /// against Flare's own container roles - see that type's remarks for why a
    /// self-referential entry is possible in principle but not expected in practice.
    /// </summary>
    Task<IReadOnlyList<ActiveService>> GetActiveServiceNamesAsync(TimeSpan window, CancellationToken cancellationToken);
}

/// <summary>One row of <see cref="ILogQueryService.GetActiveServiceNamesAsync"/>'s result.</summary>
public sealed record ActiveService(string ServiceName, DateTimeOffset LastSeenAt);

/// <summary>
/// The one component in <c>Flare.Api</c> that actually holds an
/// <see cref="IClickHouseClient"/> - everything upstream (<see cref="LogFilterSqlBuilder"/>,
/// <see cref="LogSearchQueryBuilder"/>, <see cref="LogAggregateQueryBuilder"/>) is pure
/// request-to-SQL translation with no ClickHouse dependency.
/// </summary>
/// <remarks>
/// Uses <see cref="IClickHouseClient.ExecuteReaderAsync"/> + manual
/// <see cref="ClickHouseDataReader"/> column reads by ordinal, not the newer
/// <c>QueryAsync&lt;T&gt;</c> POCO path - confirmed via reflection over the actually-restored
/// <c>ClickHouse.Driver</c> assembly which methods exist (same "verify against the real
/// assembly, don't assume" approach <c>Flare.Ingest</c>'s <c>ClickHouseLogEventWriter</c>
/// used for the write path). <c>QueryAsync&lt;T&gt;</c>'s POCO mapping requires an explicit
/// <c>RegisterPocoType&lt;T&gt;()</c> call per the package's own release notes, and its
/// property-shape requirements (a public non-init setter, with <c>required</c> members
/// only conditionally supported) don't fit this project's immutable
/// <c>record</c> DTOs cleanly - the reader path has no such registration/shape
/// requirements. Confirm this still holds during e2e verification (see this project's README).
/// </remarks>
public sealed class LogQueryService(IClickHouseClient client, TimeProvider timeProvider) : ILogQueryService
{
    public async Task<LogSearchResponse> SearchAsync(LogSearchRequest request, CancellationToken cancellationToken)
    {
        var built = LogSearchQueryBuilder.Build(request, timeProvider.GetUtcNow());

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        // LIMIT already caps the server-side result at PageSize + 1 (see
        // LogSearchQueryBuilder) - reading everything the server sent back is bounded by
        // that, no separate row-count guard needed here.
        var rows = new List<LogEventDto>();
        while (reader.Read())
        {
            rows.Add(ReadLogEvent(reader));
        }

        // A PageSize+1'th row means another page exists - trim it off and use the last
        // *returned* row's sort key as the next cursor.
        var hasMore = rows.Count > built.PageSize;
        var events = hasMore ? rows.GetRange(0, built.PageSize) : rows;
        var nextCursor = hasMore
            ? new LogSearchCursor(events[^1].Timestamp, events[^1].EventId).Encode()
            : null;

        return new LogSearchResponse { Events = events, NextCursor = nextCursor };
    }

    public async Task<LogAggregateResponse> AggregateAsync(LogAggregateRequest request, CancellationToken cancellationToken)
    {
        var built = LogAggregateQueryBuilder.Build(request, timeProvider.GetUtcNow());

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var buckets = new List<LogAggregateBucket>();
        while (reader.Read())
        {
            var bucketStart = ReadUtc(reader, 0);
            var groupKey = built.HasGroupKey ? reader.GetString(1) : null;
            var count = reader.GetFieldValue<ulong>(built.HasGroupKey ? 2 : 1);
            buckets.Add(new LogAggregateBucket { BucketStart = bucketStart, GroupKey = groupKey, Count = (long)count });
        }

        return new LogAggregateResponse { Buckets = buckets };
    }

    public async Task<LogPatternResponse> GetPatternsAsync(LogPatternRequest request, CancellationToken cancellationToken)
    {
        var built = LogPatternQueryBuilder.Build(request, timeProvider.GetUtcNow());

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var patterns = new List<LogPatternRow>();
        while (reader.Read())
        {
            patterns.Add(new LogPatternRow
            {
                PatternId = reader.GetString(0),
                Template = reader.GetString(1),
                Count = (long)reader.GetFieldValue<ulong>(2),
                ErrorCount = (long)reader.GetFieldValue<ulong>(3),
                FirstSeen = ReadUtc(reader, 4),
                LastSeen = ReadUtc(reader, 5),
            });
        }

        return new LogPatternResponse { Patterns = patterns };
    }

    public async Task<IReadOnlyList<ActiveService>> GetActiveServiceNamesAsync(TimeSpan window, CancellationToken cancellationToken)
    {
        var built = ActiveServicesQueryBuilder.Build(window, timeProvider.GetUtcNow());

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var services = new List<ActiveService>();
        while (reader.Read())
        {
            services.Add(new ActiveService(reader.GetString(0), ReadUtc(reader, 1)));
        }

        return services;
    }

    private static LogEventDto ReadLogEvent(ClickHouseDataReader reader) => new()
    {
        EventId = reader.GetGuid(0),
        Timestamp = ReadUtc(reader, 1),
        ObservedTimestamp = ReadUtc(reader, 2),
        TraceId = reader.GetString(3),
        SpanId = reader.GetString(4),
        TraceFlags = reader.GetByte(5),
        SeverityText = reader.GetString(6),
        SeverityNumber = reader.GetByte(7),
        ServiceName = reader.GetString(8),
        Body = reader.GetString(9),
        ResourceSchemaUrl = reader.GetString(10),
        ResourceAttributes = reader.GetFieldValue<Dictionary<string, string>>(11),
        ScopeSchemaUrl = reader.GetString(12),
        ScopeName = reader.GetString(13),
        ScopeVersion = reader.GetString(14),
        ScopeAttributes = reader.GetFieldValue<Dictionary<string, string>>(15),
        LogAttributes = reader.GetFieldValue<Dictionary<string, string>>(16),
        EventName = reader.GetString(17),
        PatternId = reader.GetString(18),
        PatternTemplate = reader.GetString(19),
    };

    /// <summary>
    /// <c>Timestamp</c>/<c>ObservedTimestamp</c>/bucket columns are <c>DateTime64(9)</c>
    /// with no explicit column timezone, so the driver returns <c>Kind=Unspecified</c>
    /// (preserving wall-clock exactly as stored, per the driver's own release notes).
    /// <c>Flare.Ingest</c> always writes UTC wall-clock values
    /// (<c>LogEvent.Timestamp.UtcDateTime</c>), so re-tagging as UTC here is correct for
    /// every value this schema actually contains - not a general-purpose conversion.
    /// </summary>
    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    /// <summary>
    /// Per the `clickhouse-best-practices` skill's <c>agent-query-safety</c> rule: every
    /// query gets an explicit scan/time cap rather than relying on defaults, which are
    /// unbounded on self-hosted ClickHouse.
    /// </summary>
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
