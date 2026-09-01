namespace Flare.Api.Query;

/// <summary>
/// Column list for a <c>spans</c> row, in the exact order every <c>SELECT</c> built by
/// <see cref="SpanSearchQueryBuilder"/>/<see cref="TraceByIdQueryBuilder"/> uses and
/// <see cref="SpanQueryService"/> reads back by ordinal - same convention as
/// <see cref="LogEventColumns"/>. Order matches <c>Flare.Ingest</c>'s
/// <c>ClickHouseSpanRowMapper.Columns</c> (the write side) for readability, though
/// nothing requires the two to agree - each is independently self-consistent with its
/// own reader.
/// </summary>
internal static class SpanColumns
{
    public static readonly IReadOnlyList<string> Names =
    [
        "TraceId",
        "SpanId",
        "ParentSpanId",
        "TraceState",
        "Name",
        "Kind",
        "StartTime",
        "EndTime",
        "DurationNano",
        "StatusCode",
        "StatusMessage",
        "ServiceName",
        "ResourceSchemaUrl",
        "ResourceAttributes",
        "ScopeSchemaUrl",
        "ScopeName",
        "ScopeVersion",
        "ScopeAttributes",
        "SpanAttributes",
        "`Events.TimeUnixNano`",
        "`Events.Name`",
        "`Events.Attributes`",
        "IngestedAt",
    ];

    public static readonly string SelectList = string.Join(", ", Names);
}
