using ClickHouse.Driver;
using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// <see cref="IClickHouseLogEventWriter"/> backed by <c>Aspire.ClickHouse.Driver</c>'s
/// <see cref="IClickHouseClient"/>.
/// </summary>
/// <remarks>
/// Uses <see cref="IClickHouseClient.InsertBinaryAsync(string, IEnumerable{string}, IEnumerable{object[]}, InsertOptions?, CancellationToken)"/>
/// - the driver's native RowBinary bulk-insert path (confirmed via reflection over the
/// restored <c>ClickHouse.Driver</c> assembly, not assumed) - rather than building
/// parameterized INSERT SQL text by hand. This sidesteps needing to know the driver's
/// SQL parameter-placeholder convention entirely, and is the actual "batched insert"
/// the roadmap item is named for: one call per flush batch, not one per row.
/// <c>ResourceAttributes</c>/<c>ScopeAttributes</c>/<c>LogAttributes</c> are passed as
/// <see cref="Dictionary{TKey, TValue}"/> for the DDL's <c>Map(LowCardinality(String), String)</c>
/// columns (see <see cref="ClickHouseRowMapper"/>) - the standard .NET shape for a
/// ClickHouse Map parameter; confirm this binds correctly against a real column during
/// end-to-end verification (see <c>src/Flare.Ingest/README.md</c>).
/// </remarks>
public sealed class ClickHouseLogEventWriter(IClickHouseClient client) : IClickHouseLogEventWriter
{
    private const string TableName = "logs";

    public async Task WriteBatchAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellationToken = default)
    {
        if (events.Count == 0)
        {
            return;
        }

        var rows = ClickHouseRowMapper.ToRows(events);
        await client.InsertBinaryAsync(TableName, ClickHouseRowMapper.Columns, rows, cancellationToken: cancellationToken);
    }
}
