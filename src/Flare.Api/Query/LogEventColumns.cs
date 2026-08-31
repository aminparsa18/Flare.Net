namespace Flare.Api.Query;

/// <summary>
/// Column list for a <c>logs</c> row, in the exact order every <c>SELECT</c> built by
/// <see cref="LogSearchQueryBuilder"/> uses and <see cref="LogQueryService"/> reads back
/// by ordinal - same "one list, kept in lockstep with the reading code" convention
/// <c>Flare.Ingest</c>'s <c>ClickHouseRowMapper.Columns</c> already uses for the write
/// side, mirrored here for the read side.
/// </summary>
internal static class LogEventColumns
{
    public static readonly IReadOnlyList<string> Names =
    [
        "EventId",
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
        "PatternId",
        "PatternTemplate",
        "IngestedAt",
    ];

    public static readonly string SelectList = string.Join(", ", Names);
}
