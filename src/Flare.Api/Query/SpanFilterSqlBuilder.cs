using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>A parameterized <c>WHERE</c> fragment (no leading <c>WHERE</c> keyword) plus its bound parameters.</summary>
public sealed record SpanFilterSql(string WhereSql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="SpanFilter"/> → parameterized <c>WHERE</c>-clause translation, shared
/// by <see cref="SpanSearchQueryBuilder"/>. Deliberately has no ClickHouse connection
/// dependency, same "pure function, unit-testable on its own" style as
/// <see cref="LogFilterSqlBuilder"/> - not a reuse of it, since <see cref="SpanFilter"/>
/// is its own type with genuinely different fields.
/// </summary>
/// <remarks>
/// Same parameter-binding discipline as <see cref="LogFilterSqlBuilder"/>: every
/// request-supplied value is bound via ClickHouse.Driver's <c>{name:Type}</c> parameter
/// placeholders. The only thing ever string-interpolated directly into the SQL text is
/// the attribute bag's *column name* in <see cref="ColumnFor"/>, and that only ever
/// comes from the closed <see cref="SpanAttributeBag"/> enum.
/// </remarks>
public static class SpanFilterSqlBuilder
{
    /// <summary>Default lookback applied when <see cref="SpanFilter.From"/> is omitted. Same rationale as <see cref="LogFilterSqlBuilder.DefaultLookback"/>.</summary>
    public static readonly TimeSpan DefaultLookback = TimeSpan.FromHours(1);

    public static SpanFilterSql Build(SpanFilter filter, DateTimeOffset now)
    {
        var parameters = new ClickHouseParameterCollection();
        var clauses = new List<string>();

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

        if (filter.Kinds is { Count: > 0 } kinds)
        {
            parameters.AddParameter("kinds", kinds.ToArray());
            clauses.Add("Kind IN {kinds:Array(UInt8)}");
        }

        if (filter.StatusCodes is { Count: > 0 } statusCodes)
        {
            parameters.AddParameter("statusCodes", statusCodes.ToArray());
            clauses.Add("StatusCode IN {statusCodes:Array(String)}");
        }

        if (!string.IsNullOrEmpty(filter.TraceId))
        {
            parameters.AddParameter("traceId", filter.TraceId);
            clauses.Add("TraceId = {traceId:String}");
        }

        if (filter.RootSpansOnly)
        {
            clauses.Add("ParentSpanId = ''");
        }

        if (filter.MinDurationNano is { } minDuration)
        {
            parameters.AddParameter("minDuration", minDuration);
            clauses.Add("DurationNano >= {minDuration:UInt64}");
        }

        if (filter.MaxDurationNano is { } maxDuration)
        {
            parameters.AddParameter("maxDuration", maxDuration);
            clauses.Add("DurationNano <= {maxDuration:UInt64}");
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

        return new SpanFilterSql(string.Join(" AND ", clauses), parameters);
    }

    private static string ColumnFor(SpanAttributeBag bag) => bag switch
    {
        SpanAttributeBag.Resource => "ResourceAttributes",
        SpanAttributeBag.Scope => "ScopeAttributes",
        _ => "SpanAttributes",
    };
}
