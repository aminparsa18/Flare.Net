using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>A parameterized <c>WHERE</c> fragment (no leading <c>WHERE</c> keyword) plus its bound parameters.</summary>
public sealed record LogFilterSql(string WhereSql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="LogFilter"/> → parameterized <c>WHERE</c>-clause translation, shared
/// by <see cref="LogSearchQueryBuilder"/> and <see cref="LogAggregateQueryBuilder"/>.
/// Deliberately has no ClickHouse connection dependency - same "pure function,
/// unit-testable on its own" style as <c>Flare.Ingest</c>'s <c>ClickHouseRowMapper</c>.
/// </summary>
/// <remarks>
/// Every value that can carry request-supplied text (service names, severities,
/// attribute keys/values, trace id, search term, time range) is bound as a ClickHouse
/// query parameter - <c>ClickHouse.Driver</c>'s native <c>{name:Type}</c> placeholder
/// syntax, confirmed against the package's own release notes and a live parameter-binding
/// check, not assumed (see <c>Flare.Api</c>'s README). The only thing ever
/// string-interpolated directly into the SQL text is the attribute bag's *column name*
/// in <see cref="ColumnFor"/>, and that only ever comes from the closed
/// <see cref="AttributeBag"/> enum - never from request text.
/// </remarks>
public static class LogFilterSqlBuilder
{
    /// <summary>
    /// Default lookback applied when <see cref="LogFilter.From"/> is omitted. Keeps an
    /// otherwise-unfiltered request from scanning the whole table - per the
    /// `clickhouse-best-practices` skill's <c>agent-query-safety</c> rule ("never query
    /// without bounding the scan"), reinforced server-side by
    /// <see cref="LogQueryService"/>'s <c>max_rows_to_read</c>/<c>max_execution_time</c>
    /// settings on every query.
    /// </summary>
    public static readonly TimeSpan DefaultLookback = TimeSpan.FromHours(1);

    public static LogFilterSql Build(LogFilter filter, DateTimeOffset now)
    {
        var parameters = new ClickHouseParameterCollection();
        var clauses = new List<string>();

        var from = filter.From ?? now - DefaultLookback;
        var to = filter.To ?? now;
        parameters.AddParameter("from", from.UtcDateTime);
        parameters.AddParameter("to", to.UtcDateTime);
        clauses.Add("Timestamp >= {from:DateTime64(9)}");
        clauses.Add("Timestamp < {to:DateTime64(9)}");

        if (filter.Services is { Count: > 0 } services)
        {
            parameters.AddParameter("services", services.ToArray());
            clauses.Add("ServiceName IN {services:Array(String)}");
        }

        if (filter.SeverityNumbers is { Count: > 0 } severities)
        {
            parameters.AddParameter("severities", severities.ToArray());
            clauses.Add("SeverityNumber IN {severities:Array(UInt8)}");
        }

        if (!string.IsNullOrEmpty(filter.TraceId))
        {
            parameters.AddParameter("traceId", filter.TraceId);
            clauses.Add("TraceId = {traceId:String}");
        }

        if (!string.IsNullOrEmpty(filter.SpanId))
        {
            parameters.AddParameter("spanId", filter.SpanId);
            clauses.Add("SpanId = {spanId:String}");
        }

        if (!string.IsNullOrEmpty(filter.Search))
        {
            // Pattern is fully formed client-side and bound as one parameter value,
            // rather than built server-side via concat('%', {search:String}, '%') -
            // equivalent ILIKE semantics, one fewer moving part.
            parameters.AddParameter("search", $"%{filter.Search}%");
            clauses.Add("Body ILIKE {search:String}");
        }

        if (filter.Attributes is { Count: > 0 } attributes)
        {
            for (var i = 0; i < attributes.Count; i++)
            {
                var attribute = attributes[i];
                var column = ColumnFor(attribute.Bag);
                var keyParam = $"attrKey{i}";
                var valueParam = $"attrValue{i}";
                parameters.AddParameter(keyParam, attribute.Key);
                parameters.AddParameter(valueParam, attribute.Value);
                clauses.Add($"{column}[{{{keyParam}:String}}] = {{{valueParam}:String}}");
            }
        }

        return new LogFilterSql(string.Join(" AND ", clauses), parameters);
    }

    private static string ColumnFor(AttributeBag bag) => bag switch
    {
        AttributeBag.Resource => "ResourceAttributes",
        AttributeBag.Scope => "ScopeAttributes",
        _ => "LogAttributes",
    };
}
