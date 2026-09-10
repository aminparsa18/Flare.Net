using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>A parameterized <c>WHERE</c> fragment (no leading <c>WHERE</c> keyword) plus its bound parameters.</summary>
public sealed record ExceptionFilterSql(string WhereSql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="ExceptionFilter"/> → parameterized <c>WHERE</c>-clause translation, shared
/// by <see cref="ExceptionGroupQueryBuilder"/>/<see cref="ExceptionOccurrenceQueryBuilder"/>.
/// Same "pure function, no ClickHouse connection dependency" style as
/// <see cref="SpanFilterSqlBuilder"/> - not a reuse of it, since <see cref="ExceptionFilter"/>
/// is its own, much narrower type.
/// </summary>
/// <remarks>
/// Assumes the caller's <c>FROM spans</c> clause already carries
/// <c>ARRAY JOIN Events.TimeUnixNano AS EventTime, Events.Name AS EventName,
/// Events.Attributes AS EventAttributes</c> - this only emits the <c>WHERE</c> fragment, not
/// the <c>ARRAY JOIN</c> itself, since both call sites need the exact same one and it belongs
/// next to <c>FROM</c>, not folded into a filter fragment.
/// <para>
/// Filters on the span's own <c>StartTime</c>, not the array-joined <c>EventTime</c>, for the
/// same partition-pruning reason <see cref="SpanRollupQueryBuilder"/>'s remarks document for
/// a related tradeoff: an event's timestamp always falls within its containing span's
/// <c>[StartTime, EndTime]</c> lifetime, so <c>StartTime</c>-range filtering is a safe, cheap
/// proxy that still lets <c>0007_spans.sql</c>'s monthly partitioning prune granules; callers
/// use <c>EventTime</c> itself only for precise min/max/ordering after this WHERE has already
/// narrowed the row set.
/// </para>
/// </remarks>
public static class ExceptionFilterSqlBuilder
{
    /// <summary>Default lookback applied when <see cref="ExceptionFilter.From"/> is omitted. Same 1-hour default as <see cref="SpanFilterSqlBuilder.DefaultLookback"/>/<see cref="LogFilterSqlBuilder.DefaultLookback"/>.</summary>
    public static readonly TimeSpan DefaultLookback = TimeSpan.FromHours(1);

    public static ExceptionFilterSql Build(ExceptionFilter filter, DateTimeOffset now)
    {
        var parameters = new ClickHouseParameterCollection();
        var clauses = new List<string> { "EventName = {eventName:String}" };

        parameters.AddParameter("eventName", "exception");

        var from = filter.From ?? now - DefaultLookback;
        var to = filter.To ?? now;
        parameters.AddParameter("from", from.UtcDateTime);
        parameters.AddParameter("to", to.UtcDateTime);
        clauses.Add("StartTime >= {from:DateTime64(9)}");
        clauses.Add("StartTime < {to:DateTime64(9)}");

        if (filter.Services is { Count: > 0 } services)
        {
            parameters.AddParameter("services", services.ToArray());
            clauses.Add("ServiceName IN {services:Array(String)}");
        }

        return new ExceptionFilterSql(string.Join(" AND ", clauses), parameters);
    }
}
