using ClickHouse.Driver;
using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// <see cref="IClickHouseProfileWriter"/> backed by <see cref="IClickHouseClient"/>'s RowBinary
/// bulk insert, same approach as <see cref="ClickHouseSpanWriter"/>.
/// </summary>
public sealed class ClickHouseProfileWriter(IClickHouseClient client, ClickHouseInsertColumnTypes columnTypes) : IClickHouseProfileWriter
{
    private const string TableName = "profile_samples";

    public async Task WriteBatchAsync(IReadOnlyList<ProfileSampleRecord> samples, CancellationToken cancellationToken = default)
    {
        if (samples.Count == 0)
        {
            return;
        }

        var options = await columnTypes.OptionsAsync(TableName, ClickHouseProfileRowMapper.Columns, cancellationToken: cancellationToken);
        await client.InsertBinaryAsync(TableName, ClickHouseProfileRowMapper.Columns, ClickHouseProfileRowMapper.ToRows(samples), options, cancellationToken);
    }
}
