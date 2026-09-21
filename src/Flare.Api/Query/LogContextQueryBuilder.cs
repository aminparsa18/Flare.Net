using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>One of the three <c>SELECT</c>s a context request needs, paired with its own bound parameters.</summary>
public sealed record LogContextPartSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// The three queries <see cref="LogQueryService.GetContextAsync"/> runs for
/// <c>/api/logs/context</c>: the anchor row itself (a point lookup, so a caller can tell
/// "still present" from "aged out of retention" - see <see cref="LogContextResponse.AnchorEventId"/>'s
/// remarks), the events strictly before it, and the events strictly after it.
/// </summary>
public sealed record LogContextSql(LogContextPartSql Anchor, LogContextPartSql Before, LogContextPartSql After, int BeforeLimit, int AfterLimit);

/// <summary>
/// Pure <see cref="LogContextRequest"/> → parameterized SQL builder for the Logs "context"
/// view/permalink - unit-testable on its own, same style as <see cref="LogSearchQueryBuilder"/>.
/// </summary>
/// <remarks>
/// Builds raw SQL directly rather than reusing <see cref="LogFilterSqlBuilder"/>: that
/// builder always applies <see cref="LogFilterSqlBuilder.DefaultLookback"/> when
/// <c>From</c> is omitted, which would silently exclude an old anchor event's own
/// neighbors - see <see cref="LogContextRequest"/>'s remarks for why this endpoint is
/// unfiltered/time-unbounded by design instead.
/// </remarks>
public static class LogContextQueryBuilder
{
    public const int DefaultSize = 50;
    public const int MaxSize = 200;

    public static LogContextSql Build(LogContextRequest request)
    {
        var beforeLimit = Math.Clamp(request.Before ?? DefaultSize, 1, MaxSize);
        var afterLimit = Math.Clamp(request.After ?? DefaultSize, 1, MaxSize);

        var anchorParameters = new ClickHouseParameterCollection();
        anchorParameters.AddParameter("anchorTs", request.Timestamp.UtcDateTime);
        anchorParameters.AddParameter("anchorId", request.EventId);
        var anchorSql = $"SELECT {LogEventColumns.SelectList}\n" +
            "FROM logs\n" +
            "WHERE Timestamp = {anchorTs:DateTime64(9)} AND EventId = {anchorId:UUID}\n" +
            "LIMIT 1";

        // Same "(Timestamp, EventId) < cursor, ORDER BY ... DESC" shape as
        // LogSearchQueryBuilder's own pagination - fetch one extra row so
        // LogQueryService can tell "more/HasMoreBefore" apart from "ended exactly at
        // the limit" without a separate count query.
        var beforeParameters = new ClickHouseParameterCollection();
        beforeParameters.AddParameter("anchorTs", request.Timestamp.UtcDateTime);
        beforeParameters.AddParameter("anchorId", request.EventId);
        beforeParameters.AddParameter("beforeLimit", (uint)(beforeLimit + 1));
        var beforeSql = $"SELECT {LogEventColumns.SelectList}\n" +
            "FROM logs\n" +
            "WHERE (Timestamp, EventId) < ({anchorTs:DateTime64(9)}, {anchorId:UUID})\n" +
            "ORDER BY Timestamp DESC, EventId DESC\n" +
            "LIMIT {beforeLimit:UInt64}";

        // Mirror image of Before: ordered ASC so ClickHouse returns the *nearest*
        // afterLimit+1 events rather than the furthest - LogQueryService reverses this
        // page back to the response's overall DESC order.
        var afterParameters = new ClickHouseParameterCollection();
        afterParameters.AddParameter("anchorTs", request.Timestamp.UtcDateTime);
        afterParameters.AddParameter("anchorId", request.EventId);
        afterParameters.AddParameter("afterLimit", (uint)(afterLimit + 1));
        var afterSql = $"SELECT {LogEventColumns.SelectList}\n" +
            "FROM logs\n" +
            "WHERE (Timestamp, EventId) > ({anchorTs:DateTime64(9)}, {anchorId:UUID})\n" +
            "ORDER BY Timestamp ASC, EventId ASC\n" +
            "LIMIT {afterLimit:UInt64}";

        return new LogContextSql(
            new LogContextPartSql(anchorSql, anchorParameters),
            new LogContextPartSql(beforeSql, beforeParameters),
            new LogContextPartSql(afterSql, afterParameters),
            beforeLimit,
            afterLimit);
    }
}
