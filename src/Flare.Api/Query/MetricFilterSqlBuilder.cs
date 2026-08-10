using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>A parameterized <c>WHERE</c> fragment (no leading <c>WHERE</c> keyword) plus its bound parameters.</summary>
public sealed record MetricFilterSql(string WhereSql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="MetricFilter"/> → parameterized <c>WHERE</c>-clause translation,
/// shared by <see cref="MetricNamesQueryBuilder"/> and <see cref="MetricSeriesQueryBuilder"/>.
/// Deliberately has no ClickHouse connection dependency, same style as
/// <see cref="SpanFilterSqlBuilder"/>.
/// </summary>
/// <remarks>
/// Unlike <see cref="SpanFilterSqlBuilder"/>, this produces a fragment valid against any
/// of the three point-type tables (<c>metrics_gauge</c>/<c>metrics_sum</c>/
/// <c>metrics_histogram</c>) - they share <c>Time</c>/<c>ServiceName</c>/
/// <c>DataPointAttributes</c> column names and types, so one builder covers all three
/// rather than one per table.
/// </remarks>
public static class MetricFilterSqlBuilder
{
    /// <summary>Default lookback applied when <see cref="MetricFilter.From"/> is omitted. Same rationale as <see cref="LogFilterSqlBuilder.DefaultLookback"/>.</summary>
    public static readonly TimeSpan DefaultLookback = TimeSpan.FromHours(1);

    public static MetricFilterSql Build(MetricFilter filter, DateTimeOffset now)
    {
        var parameters = new ClickHouseParameterCollection();
        var clauses = new List<string>();

        var from = filter.From ?? now - DefaultLookback;
        var to = filter.To ?? now;
        parameters.AddParameter("from", from.UtcDateTime);
        parameters.AddParameter("to", to.UtcDateTime);
        clauses.Add("Time >= {from:DateTime64(9)}");
        clauses.Add("Time < {to:DateTime64(9)}");

        if (filter.Services is { Count: > 0 } services)
        {
            parameters.AddParameter("services", services.ToArray());
            clauses.Add("ServiceName IN {services:Array(String)}");
        }

        if (filter.Attributes is { Count: > 0 } attributes)
        {
            for (var i = 0; i < attributes.Count; i++)
            {
                var attribute = attributes[i];
                var keyParam = $"attrKey{i}";
                var valueParam = $"attrValue{i}";
                parameters.AddParameter(keyParam, attribute.Key);
                parameters.AddParameter(valueParam, attribute.Value);
                clauses.Add($"DataPointAttributes[{{{keyParam}:String}}] = {{{valueParam}:String}}");
            }
        }

        return new MetricFilterSql(string.Join(" AND ", clauses), parameters);
    }
}
