using System.Data.Common;

namespace Flare.Identity.Apdex;

public sealed class DbApdexThresholdStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : IApdexThresholdStore
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
            result[reader.GetString(0)] = (int)reader.GetInt64(1);
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
            VALUES (@serviceName, @thresholdMs, @updatedAt)
            ON CONFLICT(ServiceName) DO UPDATE SET
                ThresholdMs = excluded.ThresholdMs,
                UpdatedAt = excluded.UpdatedAt
            """;
        command.AddParameter("@serviceName", serviceName);
        command.AddParameter("@thresholdMs", thresholdMs);
        command.AddParameter("@updatedAt", timeProvider.GetUtcNow().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ResetAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM ApdexThresholds WHERE ServiceName = @serviceName";
        command.AddParameter("@serviceName", serviceName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
