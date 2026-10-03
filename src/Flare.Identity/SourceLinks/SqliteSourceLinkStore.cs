using Microsoft.Data.Sqlite;

namespace Flare.Identity.SourceLinks;

public sealed class SqliteSourceLinkStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : ISourceLinkStore
{
    public async Task<IReadOnlyList<SourceLinkConfig>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ServiceName, Provider, RepoUrl, DefaultRef, PathPrefix FROM SourceLinks ORDER BY ServiceName";

        var result = new List<SourceLinkConfig>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // A provider value this build doesn't know (a row written by a newer version) is skipped, not fatal.
            if (!Enum.TryParse<SourceLinkProvider>(reader.GetString(1), out var provider))
            {
                continue;
            }

            result.Add(new SourceLinkConfig(reader.GetString(0), provider, reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        }

        return result;
    }

    public async Task SetAsync(SourceLinkConfig config, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO SourceLinks (ServiceName, Provider, RepoUrl, DefaultRef, PathPrefix, UpdatedAt)
            VALUES ($serviceName, $provider, $repoUrl, $defaultRef, $pathPrefix, $updatedAt)
            ON CONFLICT(ServiceName) DO UPDATE SET
                Provider = excluded.Provider,
                RepoUrl = excluded.RepoUrl,
                DefaultRef = excluded.DefaultRef,
                PathPrefix = excluded.PathPrefix,
                UpdatedAt = excluded.UpdatedAt
            """;
        command.Parameters.AddWithValue("$serviceName", config.ServiceName);
        command.Parameters.AddWithValue("$provider", config.Provider.ToString());
        command.Parameters.AddWithValue("$repoUrl", config.RepoUrl);
        command.Parameters.AddWithValue("$defaultRef", config.DefaultRef);
        command.Parameters.AddWithValue("$pathPrefix", config.PathPrefix);
        command.Parameters.AddWithValue("$updatedAt", timeProvider.GetUtcNow().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SourceLinks WHERE ServiceName = $serviceName";
        command.Parameters.AddWithValue("$serviceName", serviceName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
