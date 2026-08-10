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
        await client.InsertBinaryAsync(TableName, ClickHouseSpanRowMapper.Columns, rows, cancellationToken: cancellationToken);
    }
}
