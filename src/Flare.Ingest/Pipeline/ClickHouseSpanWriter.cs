using ClickHouse.Driver;
using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// <see cref="IClickHouseSpanWriter"/> backed by <c>Aspire.ClickHouse.Driver</c>'s
/// <see cref="IClickHouseClient"/>, same RowBinary bulk-insert approach as
/// <see cref="ClickHouseLogEventWriter"/> - see its remarks for why
/// <c>InsertBinaryAsync</c> over hand-built SQL. The <c>Events</c> Nested column's three
/// desugared array columns and the <c>StatusCode</c> Enum8 column round-trip through
/// this same call with no special-casing needed - confirmed via a live spike before
/// writing this class (see <see cref="ClickHouseSpanRowMapper"/>'s remarks).
/// </summary>
public sealed class ClickHouseSpanWriter(IClickHouseClient client) : IClickHouseSpanWriter
{
    private const string TableName = "spans";

    public async Task WriteBatchAsync(IReadOnlyList<SpanRecord> spans, CancellationToken cancellationToken = default)
    {
        if (spans.Count == 0)
        {
            return;
        }

        var rows = ClickHouseSpanRowMapper.ToRows(spans);
        await client.InsertBinaryAsync(TableName, ClickHouseSpanRowMapper.Columns, rows, SpanInsertOptions(), cancellationToken);
    }

    /// <summary>
    /// <c>materialized_views_ignore_errors</c> keeps a failure in the ADR-0030
    /// <c>service_metrics_mv</c> materialized view (<c>db/clickhouse/0022_service_metrics.sql</c>)
    /// from failing this insert into <c>spans</c> itself - ClickHouse's default behavior is
    /// to fail the whole originating insert if a dependent materialized view's own insert
    /// fails, which would otherwise couple the Services-tab RED-metrics rollup's failure
    /// domain to span ingestion. Confirmed via a live spike before shipping (same practice
    /// this class's own remarks already document for the RowBinary round-trip itself).
    /// </summary>
    private static InsertOptions SpanInsertOptions() => new()
    {
        CustomSettings = new Dictionary<string, object>
        {
            ["materialized_views_ignore_errors"] = 1,
        },
    };
}
