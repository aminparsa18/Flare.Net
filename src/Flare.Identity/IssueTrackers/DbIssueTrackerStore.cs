namespace Flare.Identity.IssueTrackers;

public sealed class DbIssueTrackerStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : IIssueTrackerStore
{
    public async Task<IReadOnlyList<IssueTrackerConfig>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT ServiceName, Provider, SiteUrl, ProjectId, IssueTypeId FROM IssueTrackers ORDER BY ServiceName";

        var result = new List<IssueTrackerConfig>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            // A provider value this build doesn't know (a row written by a newer version) is skipped, not fatal.
            if (!Enum.TryParse<IssueTrackerProvider>(reader.GetString(1), out var provider))
            {
                continue;
            }

            result.Add(new IssueTrackerConfig(reader.GetString(0), provider, reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        }

        return result;
    }

    public async Task SetAsync(IssueTrackerConfig config, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO IssueTrackers (ServiceName, Provider, SiteUrl, ProjectId, IssueTypeId, UpdatedAt)
            VALUES (@serviceName, @provider, @siteUrl, @projectId, @issueTypeId, @updatedAt)
            ON CONFLICT(ServiceName) DO UPDATE SET
                Provider = excluded.Provider,
                SiteUrl = excluded.SiteUrl,
                ProjectId = excluded.ProjectId,
                IssueTypeId = excluded.IssueTypeId,
                UpdatedAt = excluded.UpdatedAt
            """;
        command.AddParameter("@serviceName", config.ServiceName);
        command.AddParameter("@provider", config.Provider.ToString());
        command.AddParameter("@siteUrl", config.SiteUrl);
        command.AddParameter("@projectId", config.ProjectId);
        command.AddParameter("@issueTypeId", config.IssueTypeId);
        command.AddParameter("@updatedAt", timeProvider.GetUtcNow().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM IssueTrackers WHERE ServiceName = @serviceName";
        command.AddParameter("@serviceName", serviceName);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
