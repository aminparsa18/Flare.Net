using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Readers;
using Flare.Api.Model;
using Flare.Api.Query.LogQl;

namespace Flare.Api.Query;

public interface ILogQueryService
{
    Task<LogSearchResponse> SearchAsync(LogSearchRequest request, CancellationToken cancellationToken);

    Task<LogAggregateResponse> AggregateAsync(LogAggregateRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Runs a parsed SQL-query-row query - the Logs page's SQL-query feature. Throws
    /// <see cref="Query.LogQl.LogQlParseException"/> for invalid query text (the endpoint
    /// turns that into a 400 with the exception's message).
    /// </summary>
    Task<LogQlQueryResponse> RunQlQueryAsync(LogQlQueryRequest request, CancellationToken cancellationToken);

    /// <summary>Ranked Drain clusters ("log patterns") within the request's filter window - see <see cref="LogPatternQueryBuilder"/>.</summary>
    Task<LogPatternResponse> GetPatternsAsync(LogPatternRequest request, CancellationToken cancellationToken);

    /// <summary>Every <c>LogAttributes</c> key that parses as numeric on at least one in-scope event - the Value distribution chart's attribute picker. See <see cref="LogAttributeKeysQueryBuilder"/>.</summary>
    Task<LogAttributeKeysResponse> GetNumericAttributeKeysAsync(LogAttributeKeysRequest request, CancellationToken cancellationToken);

    /// <summary>A random sample of one numeric <c>LogAttributes</c> key's values over time - the Value distribution chart's data. See <see cref="LogValueDistributionQueryBuilder"/>.</summary>
    Task<LogValueDistributionResponse> GetValueDistributionAsync(LogValueDistributionRequest request, CancellationToken cancellationToken);

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

        if (request.IncludeSpanDuration && events.Count > 0)
        {
            events = await WithSpanDurationsAsync(events, cancellationToken);
        }

        return new LogSearchResponse { Events = events, NextCursor = nextCursor };
    }

    /// <summary>
    /// Follow-up query for <see cref="LogSearchRequest.IncludeSpanDuration"/> - same
    /// "bounded query over just this page's keys, not a join over the whole matched
    /// result set" shape as <see cref="SpanQueryService"/>'s own span-count follow-up,
    /// but matching exact <c>(TraceId, SpanId)</c> *pairs* rather than a single TraceId
    /// column - see <see cref="SpanDurationQueryBuilder"/>'s remarks for why the SQL
    /// itself is looser (TraceId-IN/SpanId-IN, not a true pair-IN) and why that's still
    /// correct here.
    /// </summary>
    private async Task<List<LogEventDto>> WithSpanDurationsAsync(List<LogEventDto> events, CancellationToken cancellationToken)
    {
        // Logs use the same empty-string-means-absent convention as every other
        // nullable-ish string here (see this file's class remarks) - a log with no
        // trace context can't correlate to any span, so it's excluded before the query
        // is even built rather than sent as a pointless '' lookup.
        var pairs = events
            .Where(e => e.TraceId.Length > 0 && e.SpanId.Length > 0)
            .Select(e => (e.TraceId, e.SpanId))
            .Distinct()
            .ToList();

        if (pairs.Count == 0)
        {
            return events;
        }

        var built = SpanDurationQueryBuilder.Build(pairs);

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var durations = new Dictionary<(string TraceId, string SpanId), ulong>();
        while (reader.Read())
        {
            durations[(reader.GetString(0), reader.GetString(1))] = reader.GetFieldValue<ulong>(2);
        }

        // A pair absent from `durations` means either the log has trace context but its
        // enclosing span hasn't flushed to ClickHouse yet (the common case - see
        // Planning.md's "Logs: correlate a log event to its enclosing span's duration"
        // entry), or no such span was ever emitted - both cases fall back to null (no
        // duration shown), not a sentinel value.
        return events.ConvertAll(e =>
            durations.TryGetValue((e.TraceId, e.SpanId), out var duration)
                ? e with { SpanDurationNano = duration }
                : e);
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
            buckets.Add(new LogAggregateBucket { BucketStart = bucketStart, GroupKey = groupKey, Count = count });
        }

        return new LogAggregateResponse { Buckets = buckets };
    }

    public async Task<LogQlQueryResponse> RunQlQueryAsync(LogQlQueryRequest request, CancellationToken cancellationToken)
    {
        // LogQlParseException propagates as-is - HandleQlQueryAsync turns it into a 400
        // with the message intact, same "let the endpoint map the exception" shape
        // AggregateAsync's own ArgumentOutOfRangeException already relies on.
        var built = LogQlQueryBuilder.Build(request, timeProvider.GetUtcNow());

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        switch (built.Kind)
        {
            case LogQlDispatchKind.Count:
            {
                // Every aggregate select (count()/avg()/sum()) is wrapped in toFloat64(...)
                // by LogQlQueryBuilder, so this is always a plain double read - see its own
                // remarks. avg() over zero matching rows is NaN (0.0/0.0, not an error - a
                // bare aggregate query with no GROUP BY still always returns exactly one
                // row), which System.Text.Json can't serialize - sanitized to 0 here rather
                // than surfacing as a 500 for an otherwise-valid "nothing matched" query.
                var raw = reader.Read() ? reader.GetDouble(0) : 0;
                var count = double.IsNaN(raw) || double.IsInfinity(raw) ? 0 : raw;
                return new LogQlQueryResponse { Kind = LogQlResultKind.Count, Count = count };
            }

            case LogQlDispatchKind.Series:
            {
                // Same toFloat64(...) wrapping as the Count case above - a plain double
                // read regardless of which aggregate ran. Never NaN here: GROUP BY only
                // ever emits a row for a bucket that actually has >=1 matching event, so
                // avg()'s divisor can't be zero within an emitted bucket.
                var buckets = new List<LogAggregateBucket>();
                while (reader.Read())
                {
                    var bucketStart = ReadUtc(reader, 0);
                    var groupKey = built.HasGroupKey ? reader.GetString(1) : null;
                    var value = reader.GetDouble(built.HasGroupKey ? 2 : 1);
                    buckets.Add(new LogAggregateBucket { BucketStart = bucketStart, GroupKey = groupKey, Count = value });
                }

                return new LogQlQueryResponse { Kind = LogQlResultKind.Series, Buckets = buckets };
            }

            case LogQlDispatchKind.Rows:
            {
                // See LogQlQueryBuilder.RawRowLimit's remarks - BuildFromFilterSql asked
                // for one extra row (same "detect another page exists" trick SearchAsync
                // uses), so more than the limit back means more rows exist beyond it.
                var rows = new List<LogEventDto>();
                while (reader.Read())
                {
                    rows.Add(ReadLogEvent(reader));
                }

                var hasMore = rows.Count > LogQlQueryBuilder.RawRowLimit;
                var events = hasMore ? rows.GetRange(0, LogQlQueryBuilder.RawRowLimit) : rows;
                return new LogQlQueryResponse { Kind = LogQlResultKind.Rows, Events = events, HasMoreRows = hasMore };
            }

            default: // Table
            {
                // Same "ask for one extra row" trick as Rows above. Every selected column
                // here is one of the fixed scalar String/UInt8 columns LogQlColumn allows
                // (see LogQlWhereTranslator.ColumnName) - generic ToString() reads are safe,
                // no Map(String, String) attribute-bag columns are selectable this way.
                var columnCount = built.Columns?.Count ?? 0;
                var rawRows = new List<List<string>>();
                while (reader.Read())
                {
                    var row = new List<string>(columnCount);
                    for (var i = 0; i < columnCount; i++)
                    {
                        row.Add(reader.IsDBNull(i) ? string.Empty : reader.GetValue(i)?.ToString() ?? string.Empty);
                    }

                    rawRows.Add(row);
                }

                var hasMoreRows = rawRows.Count > LogQlQueryBuilder.RawRowLimit;
                var tableRows = hasMoreRows ? rawRows.GetRange(0, LogQlQueryBuilder.RawRowLimit) : rawRows;
                return new LogQlQueryResponse
                {
                    Kind = LogQlResultKind.Table,
                    Columns = built.Columns,
                    Rows = tableRows,
                    HasMoreRows = hasMoreRows,
                };
            }
        }
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

    public async Task<LogAttributeKeysResponse> GetNumericAttributeKeysAsync(LogAttributeKeysRequest request, CancellationToken cancellationToken)
    {
        var built = LogAttributeKeysQueryBuilder.Build(request, timeProvider.GetUtcNow());

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var keys = new List<LogAttributeKeyInfo>();
        while (reader.Read())
        {
            keys.Add(new LogAttributeKeyInfo { Key = reader.GetString(0), NumericCount = (long)reader.GetFieldValue<ulong>(1) });
        }

        return new LogAttributeKeysResponse { Keys = keys };
    }

    public async Task<LogValueDistributionResponse> GetValueDistributionAsync(LogValueDistributionRequest request, CancellationToken cancellationToken)
    {
        var built = LogValueDistributionQueryBuilder.Build(request, timeProvider.GetUtcNow());

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var points = new List<LogValueDistributionPoint>();
        while (reader.Read())
        {
            points.Add(new LogValueDistributionPoint { Timestamp = ReadUtc(reader, 0), Value = reader.GetDouble(1) });
        }

        return new LogValueDistributionResponse { Points = points };
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
        IngestedAt = ReadUtc(reader, 20),
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
