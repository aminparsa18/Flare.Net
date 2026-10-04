using System.Data.Common;

namespace Flare.Identity.MetricMetadata;

public sealed class DbMetricMetadataOverrideStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : IMetricMetadataOverrideStore
{
    public async Task<IReadOnlyDictionary<string, MetricMetadataOverride>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MetricName, Unit, Description, TreatAsCounter FROM MetricMetadataOverrides";

        var result = new Dictionary<string, MetricMetadataOverride>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var metricName = reader.GetString(0);
            result[metricName] = new MetricMetadataOverride(
                metricName,
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt64(3) != 0);
        }

        return result;
    }

    public async Task SetAsync(MetricMetadataOverride metadataOverride, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO MetricMetadataOverrides (MetricName, Unit, Description, TreatAsCounter, UpdatedAt)
            VALUES (@metricName, @unit, @description, @treatAsCounter, @updatedAt)
            ON CONFLICT(MetricName) DO UPDATE SET
                Unit = excluded.Unit,
                Description = excluded.Description,
                TreatAsCounter = excluded.TreatAsCounter,
                UpdatedAt = excluded.UpdatedAt
            """;
        command.AddParameter("@metricName", metadataOverride.MetricName);
        command.AddParameter("@unit", (object?)metadataOverride.Unit ?? DBNull.Value);
        command.AddParameter("@description", (object?)metadataOverride.Description ?? DBNull.Value);
        command.AddParameter("@treatAsCounter", metadataOverride.TreatAsCounter ? 1 : 0);
        command.AddParameter("@updatedAt", timeProvider.GetUtcNow().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ResetAsync(string metricName, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM MetricMetadataOverrides WHERE MetricName = @metricName";
        command.AddParameter("@metricName", metricName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
