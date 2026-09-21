using Microsoft.Data.Sqlite;

namespace Flare.Identity.Apdex;

public sealed class SqliteApdexThresholdStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : IApdexThresholdStore
{
    public async Task<IReadOnlyDictionary<string, int>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ServiceName, ThresholdMs FROM ApdexThresholds";

        var result = new Dictionary<string, int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[reader.GetString(0)] = reader.GetInt32(1);
        }

        return result;
    }

    public async Task SetAsync(string serviceName, int thresholdMs, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO ApdexThresholds (ServiceName, ThresholdMs, UpdatedAt)
            VALUES ($serviceName, $thresholdMs, $updatedAt)
            ON CONFLICT(ServiceName) DO UPDATE SET
                ThresholdMs = excluded.ThresholdMs,
                UpdatedAt = excluded.UpdatedAt
            """;
        command.Parameters.AddWithValue("$serviceName", serviceName);
        command.Parameters.AddWithValue("$thresholdMs", thresholdMs);
        command.Parameters.AddWithValue("$updatedAt", timeProvider.GetUtcNow().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ResetAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ApdexThresholds WHERE ServiceName = $serviceName";
        command.Parameters.AddWithValue("$serviceName", serviceName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
