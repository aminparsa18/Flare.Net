using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// Pure <see cref="SpanRecord"/> → ClickHouse row mapping for the
/// <c>clickhousedb.spans</c> table (<c>db/clickhouse/0007_spans.sql</c>). Deliberately
/// has no ClickHouse connection dependency, same style as <see cref="ClickHouseRowMapper"/>.
/// </summary>
/// <remarks>
/// <c>Events</c> is a <c>Nested</c> column in the DDL, which ClickHouse desugars into
/// three parallel <c>Array(...)</c> columns (<c>Events.TimeUnixNano</c>/<c>Events.Name</c>/
/// <c>Events.Attributes</c>) - confirmed via a live spike against
/// <c>Aspire.ClickHouse.Driver</c> that <see cref="ClickHouse.Driver.IClickHouseClient.InsertBinaryAsync"/>
/// accepts these as plain <c>DateTime[]</c>/<c>string[]</c>/<c>Dictionary&lt;string,string&gt;[]</c>
/// row values, and that <c>ExecuteReaderAsync</c> reads them back as the same native
/// array types - no special handling needed beyond building the three parallel arrays
/// here, same way <see cref="ClickHouseRowMapper"/> builds <c>Dictionary&lt;string,string&gt;</c>
/// for the plain <c>Map</c> columns.
///
/// <c>StatusCode</c> is inserted as the enum's string label (e.g. <c>"STATUS_CODE_ERROR"</c>),
/// not its ordinal byte - the same spike confirmed the driver accepts either for an
/// <c>Enum8</c> column, but reads always return the label, so writing the label keeps
/// insert and read symmetric.
/// </remarks>
public static class ClickHouseSpanRowMapper
{
    private static readonly string[] StatusCodeLabels = ["STATUS_CODE_UNSET", "STATUS_CODE_OK", "STATUS_CODE_ERROR"];

    /// <summary>
    /// Column names in the exact order every row's values must agree on - see
    /// <see cref="ClickHouseRowMapper.Columns"/>'s remarks for why order matters to
    /// <c>InsertBinaryAsync</c>. Matches <c>0007_spans.sql</c>'s declaration order, with
    /// the <c>Events</c> Nested column's three desugared array columns listed last.
    /// </summary>
    public static readonly IReadOnlyList<string> Columns =
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
        "Events.TimeUnixNano",
        "Events.Name",
        "Events.Attributes",
    ];

    /// <summary>Maps a single <see cref="SpanRecord"/> to a row, positionally matching <see cref="Columns"/>.</summary>
    public static object[] ToRow(SpanRecord span) =>
    [
        span.TraceId,
        span.SpanId,
        span.ParentSpanId ?? string.Empty,
        span.TraceState ?? string.Empty,
        span.Name ?? string.Empty,
        (byte)span.Kind,
        span.StartTime.UtcDateTime,
        span.EndTime.UtcDateTime,
        span.DurationNano,
        StatusCodeLabel(span.StatusCode),
        span.StatusMessage ?? string.Empty,
        span.ServiceName ?? string.Empty,
        span.ResourceSchemaUrl ?? string.Empty,
        new Dictionary<string, string>(span.ResourceAttributes),
        span.ScopeSchemaUrl ?? string.Empty,
        span.ScopeName ?? string.Empty,
        span.ScopeVersion ?? string.Empty,
        new Dictionary<string, string>(span.ScopeAttributes),
        new Dictionary<string, string>(span.SpanAttributes),
        span.Events.Select(e => e.Timestamp.UtcDateTime).ToArray(),
        span.Events.Select(e => e.Name ?? string.Empty).ToArray(),
        span.Events.Select(e => new Dictionary<string, string>(e.Attributes)).ToArray(),
    ];

    /// <summary>Maps a batch of spans to rows, in the same order.</summary>
    public static IReadOnlyList<object[]> ToRows(IReadOnlyList<SpanRecord> spans)
    {
        var rows = new object[spans.Count][];
        for (var i = 0; i < spans.Count; i++)
        {
            rows[i] = ToRow(spans[i]);
        }
        return rows;
    }

    /// <summary>
    /// Maps OTLP's Status.Code int (0=unset, 1=ok, 2=error) to the DDL's Enum8 label.
    /// Falls back to unset for any out-of-range value rather than throwing - a
    /// forward-compat guard against a future OTLP spec adding a status code Flare
    /// doesn't know about yet.
    /// </summary>
    private static string StatusCodeLabel(int statusCode) =>
        statusCode >= 0 && statusCode < StatusCodeLabels.Length ? StatusCodeLabels[statusCode] : StatusCodeLabels[0];
}
