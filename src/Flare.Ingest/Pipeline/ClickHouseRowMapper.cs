using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Pure <see cref="LogEvent"/> → ClickHouse row mapping for the <c>clickhousedb.logs</c>
/// table (<c>db/clickhouse/0001_logs.sql</c>). Deliberately has no ClickHouse connection
/// dependency so it's unit-testable on its own, same style as <c>OtlpLogMapper</c>.
/// </summary>
/// <remarks>
/// Every DDL column is non-nullable, but most corresponding <see cref="LogEvent"/>
/// properties are nullable strings (OTLP can't distinguish "unset" from "empty" on the
/// wire - see <see cref="LogEvent"/>'s own remarks). This mapper is where that gap gets
/// closed: every nullable string becomes <see cref="string.Empty"/>, and
/// <see cref="LogEvent.ObservedTimestamp"/> falls back to <see cref="LogEvent.Timestamp"/>
/// - exactly the two conventions <c>0001_logs.sql</c>'s comments call out as "the
/// inserting sink's job".
/// </remarks>
public static class ClickHouseRowMapper
{
    /// <summary>
    /// Column names in the exact order this list and every row's values must agree on -
    /// <see cref="ClickHouse.Driver.IClickHouseClient.InsertBinaryAsync"/> builds the
    /// RowBinary insert against this column order, so <see cref="ToRow"/>'s array must
    /// produce values in the same order. Matches <c>0001_logs.sql</c>'s declaration
    /// order, with <c>EventId</c>, then <c>PatternId</c>/<c>PatternTemplate</c>, appended
    /// in that order - matching how each was added via its own
    /// <c>ALTER TABLE ... ADD COLUMN</c> migration (<c>0002_logs_event_id.sql</c>, then
    /// <c>0010_logs_pattern.sql</c>).
    /// </summary>
    public static readonly IReadOnlyList<string> Columns =
    [
        "Timestamp",
        "ObservedTimestamp",
        "TraceId",
        "SpanId",
        "TraceFlags",
        "SeverityText",
        "SeverityNumber",
        "ServiceName",
        "Body",
        "ResourceSchemaUrl",
        "ResourceAttributes",
        "ScopeSchemaUrl",
        "ScopeName",
        "ScopeVersion",
        "ScopeAttributes",
        "LogAttributes",
        "EventName",
        "EventId",
        "PatternId",
        "PatternTemplate",
    ];

    /// <summary>Maps a single <see cref="LogEvent"/> to a row, positionally matching <see cref="Columns"/>.</summary>
    public static object[] ToRow(LogEvent logEvent) =>
    [
        logEvent.Timestamp.UtcDateTime,
        (logEvent.ObservedTimestamp ?? logEvent.Timestamp).UtcDateTime,
        logEvent.TraceId ?? string.Empty,
        logEvent.SpanId ?? string.Empty,
        logEvent.TraceFlags,
        logEvent.SeverityText ?? string.Empty,
        (byte)logEvent.SeverityNumber,
        logEvent.ServiceName ?? string.Empty,
        logEvent.Body ?? string.Empty,
        logEvent.ResourceSchemaUrl ?? string.Empty,
        new Dictionary<string, string>(logEvent.ResourceAttributes),
        logEvent.ScopeSchemaUrl ?? string.Empty,
        logEvent.ScopeName ?? string.Empty,
        logEvent.ScopeVersion ?? string.Empty,
        new Dictionary<string, string>(logEvent.ScopeAttributes),
        new Dictionary<string, string>(logEvent.LogAttributes),
        logEvent.EventName ?? string.Empty,
        logEvent.EventId,
        logEvent.PatternId,
        logEvent.PatternTemplate,
    ];

    /// <summary>Maps a batch of events to rows, in the same order.</summary>
    public static IReadOnlyList<object[]> ToRows(IReadOnlyList<LogEvent> events)
    {
        var rows = new object[events.Count][];
        for (var i = 0; i < events.Count; i++)
        {
            rows[i] = ToRow(events[i]);
        }
        return rows;
    }
}
