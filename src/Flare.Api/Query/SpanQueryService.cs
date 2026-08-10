using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Readers;
using Flare.Api.Model;

namespace Flare.Api.Query;

public interface ISpanQueryService
{
    Task<SpanSearchResponse> SearchAsync(SpanSearchRequest request, CancellationToken cancellationToken);

    /// <summary>Every span sharing <paramref name="traceId"/>, for the waterfall view. Returns <see langword="null"/> if no spans match.</summary>
    Task<TraceDto?> GetTraceAsync(string traceId, CancellationToken cancellationToken);
}

/// <summary>
/// The one component holding an <see cref="IClickHouseClient"/> for spans - same role as
/// <see cref="LogQueryService"/>, deliberately a separate sibling class rather than a
/// shared/generic one (the read shapes diverge enough - Nested array columns, a
/// different cursor tuple - that forcing a shared base would cost more clarity than it
/// saves for two implementations).
/// </summary>
/// <remarks>
/// Same <c>ExecuteReaderAsync</c> + manual <see cref="ClickHouseDataReader"/> ordinal
/// reads as <see cref="LogQueryService"/>, not <c>QueryAsync&lt;T&gt;</c> - see that
/// class's remarks for why. The <c>Events</c> Nested column's three desugared array
/// columns read back as plain <c>DateTime[]</c>/<c>string[]</c>/
/// <c>Dictionary&lt;string,string&gt;[]</c> via <c>GetFieldValue&lt;T&gt;</c> - confirmed
/// by the same live spike that de-risked the write side (see
/// <c>Flare.Ingest</c>'s <c>ClickHouseSpanRowMapper</c> remarks) and re-confirmed against
/// real inserted data during this roadmap slice's Pass 2 gate.
/// </remarks>
public sealed class SpanQueryService(IClickHouseClient client, TimeProvider timeProvider) : ISpanQueryService
{
    public async Task<SpanSearchResponse> SearchAsync(SpanSearchRequest request, CancellationToken cancellationToken)
    {
        var built = SpanSearchQueryBuilder.Build(request, timeProvider.GetUtcNow());

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var rows = new List<SpanDto>();
        while (reader.Read())
        {
            rows.Add(ReadSpan(reader));
        }

        // A PageSize+1'th row means another page exists - same trim-and-derive-cursor
        // trick as LogQueryService.SearchAsync.
        var hasMore = rows.Count > built.PageSize;
        var spans = hasMore ? rows.GetRange(0, built.PageSize) : rows;
        var nextCursor = hasMore
            ? new SpanSearchCursor(spans[^1].StartTime, spans[^1].TraceId, spans[^1].SpanId).Encode()
            : null;

        return new SpanSearchResponse { Spans = spans, NextCursor = nextCursor };
    }

    public async Task<TraceDto?> GetTraceAsync(string traceId, CancellationToken cancellationToken)
    {
        var built = TraceByIdQueryBuilder.Build(traceId);

        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, SafetyOptions(), cancellationToken);

        var spans = new List<SpanDto>();
        while (reader.Read())
        {
            spans.Add(ReadSpan(reader));
        }

        return spans.Count == 0 ? null : new TraceDto { TraceId = traceId, Spans = spans };
    }

    private static SpanDto ReadSpan(ClickHouseDataReader reader)
    {
        var eventTimes = reader.GetFieldValue<DateTime[]>(19);
        var eventNames = reader.GetFieldValue<string[]>(20);
        var eventAttributes = reader.GetFieldValue<Dictionary<string, string>[]>(21);

        var events = new List<SpanEventDto>(eventTimes.Length);
        for (var i = 0; i < eventTimes.Length; i++)
        {
            events.Add(new SpanEventDto
            {
                Timestamp = new DateTimeOffset(DateTime.SpecifyKind(eventTimes[i], DateTimeKind.Utc)),
                Name = eventNames[i],
                Attributes = eventAttributes[i],
            });
        }

        return new SpanDto
        {
            TraceId = reader.GetString(0),
            SpanId = reader.GetString(1),
            ParentSpanId = reader.GetString(2),
            TraceState = reader.GetString(3),
            Name = reader.GetString(4),
            Kind = reader.GetByte(5),
            StartTime = ReadUtc(reader, 6),
            EndTime = ReadUtc(reader, 7),
            DurationNano = reader.GetFieldValue<ulong>(8),
            StatusCode = reader.GetString(9),
            StatusMessage = reader.GetString(10),
            ServiceName = reader.GetString(11),
            ResourceSchemaUrl = reader.GetString(12),
            ResourceAttributes = reader.GetFieldValue<Dictionary<string, string>>(13),
            ScopeSchemaUrl = reader.GetString(14),
            ScopeName = reader.GetString(15),
            ScopeVersion = reader.GetString(16),
            ScopeAttributes = reader.GetFieldValue<Dictionary<string, string>>(17),
            SpanAttributes = reader.GetFieldValue<Dictionary<string, string>>(18),
            Events = events,
        };
    }

    /// <summary>Same UTC re-tagging rationale as <see cref="LogQueryService"/>'s own <c>ReadUtc</c> - <c>Flare.Ingest</c> always writes UTC wall-clock values.</summary>
    private static DateTimeOffset ReadUtc(ClickHouseDataReader reader, int ordinal) =>
        new(DateTime.SpecifyKind(reader.GetDateTime(ordinal), DateTimeKind.Utc));

    /// <summary>Same scan/time safety cap as <see cref="LogQueryService.SafetyOptions"/>.</summary>
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
