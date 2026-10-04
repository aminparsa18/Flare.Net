using System.Data.Common;

namespace Flare.Identity.SourceLinks;

public sealed class DbSourceLinkStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : ISourceLinkStore
{
    public async Task<IReadOnlyList<SourceLinkConfig>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ServiceName, Provider, RepoUrl, DefaultRef, PathPrefix, AccessToken FROM SourceLinks ORDER BY ServiceName";

        var result = new List<SourceLinkConfig>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // A provider value this build doesn't know (a row written by a newer version) is skipped, not fatal.
            if (!Enum.TryParse<SourceLinkProvider>(reader.GetString(1), out var provider))
            {
                continue;
            }

            result.Add(new SourceLinkConfig(reader.GetString(0), provider, reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return result;
    }

    public async Task SetAsync(SourceLinkConfig config, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO SourceLinks (ServiceName, Provider, RepoUrl, DefaultRef, PathPrefix, AccessToken, UpdatedAt)
            VALUES (@serviceName, @provider, @repoUrl, @defaultRef, @pathPrefix, @accessToken, @updatedAt)
            ON CONFLICT(ServiceName) DO UPDATE SET
                Provider = excluded.Provider,
                RepoUrl = excluded.RepoUrl,
                DefaultRef = excluded.DefaultRef,
                PathPrefix = excluded.PathPrefix,
                AccessToken = CASE WHEN @clearToken = 1 THEN NULL ELSE COALESCE(excluded.AccessToken, SourceLinks.AccessToken) END,
                UpdatedAt = excluded.UpdatedAt
            """;
        command.AddParameter("@serviceName", config.ServiceName);
        command.AddParameter("@provider", config.Provider.ToString());
        command.AddParameter("@repoUrl", config.RepoUrl);
        command.AddParameter("@defaultRef", config.DefaultRef);
        command.AddParameter("@pathPrefix", config.PathPrefix);
        command.AddParameter("@accessToken", string.IsNullOrEmpty(config.AccessToken) ? DBNull.Value : config.AccessToken);
        command.AddParameter("@clearToken", config.AccessToken == "" ? 1 : 0);
        command.AddParameter("@updatedAt", timeProvider.GetUtcNow().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM SourceLinks WHERE ServiceName = @serviceName";
        command.AddParameter("@serviceName", serviceName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
