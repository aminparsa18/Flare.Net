using ClickHouse.Driver;
using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// <see cref="IClickHouseProfileWriter"/> backed by <see cref="IClickHouseClient"/>'s RowBinary
/// bulk insert, same approach as <see cref="ClickHouseSpanWriter"/>.
/// </summary>
public sealed class ClickHouseProfileWriter(IClickHouseClient client) : IClickHouseProfileWriter
{
    private const string TableName = "profile_samples";

    public async Task WriteBatchAsync(IReadOnlyList<ProfileSampleRecord> samples, CancellationToken cancellationToken = default)
    {
        if (samples.Count == 0)
        {
            return;
        }

        await client.InsertBinaryAsync(TableName, ClickHouseProfileRowMapper.Columns, ClickHouseProfileRowMapper.ToRows(samples), cancellationToken: cancellationToken);
    }
}
