using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>A parameterized one-row rank/quantiles query plus its bound parameters.</summary>
public sealed record SpanDurationPercentileSql(string Sql, ClickHouseParameterCollection Parameters);

/// <summary>
/// Pure <see cref="SpanDurationPercentileRequest"/> -> SQL: one scan over spans with the
/// same <c>ServiceName</c> + <c>Name</c> within <see cref="WindowHalfWidth"/> of the span's
/// start, returning the sample count, how many are no longer than this span, and
/// p50/p95/p99. The window is fixed (not caller-chosen) so the result is comparable across
/// spans and the scan stays bounded to two hours of one table.
/// </summary>
public static class SpanDurationPercentileQueryBuilder
{
    public static readonly TimeSpan WindowHalfWidth = TimeSpan.FromHours(1);

    public static SpanDurationPercentileSql Build(SpanDurationPercentileRequest request)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("serviceName", request.ServiceName);
        parameters.AddParameter("spanName", request.Name);
        parameters.AddParameter("duration", request.DurationNano);
        parameters.AddParameter("from", (request.StartTime - WindowHalfWidth).UtcDateTime);
        parameters.AddParameter("to", (request.StartTime + WindowHalfWidth).UtcDateTime);

        const string sql = "SELECT count() AS Total,\n" +
            "    countIf(DurationNano <= {duration:UInt64}) AS AtOrBelow,\n" +
            "    quantile(0.5)(DurationNano) AS P50,\n" +
            "    quantile(0.95)(DurationNano) AS P95,\n" +
            "    quantile(0.99)(DurationNano) AS P99\n" +
            "FROM spans\n" +
            "WHERE ServiceName = {serviceName:String} AND Name = {spanName:String}\n" +
            "    AND StartTime >= {from:DateTime64(9)} AND StartTime < {to:DateTime64(9)}";

        return new SpanDurationPercentileSql(sql, parameters);
    }
}
