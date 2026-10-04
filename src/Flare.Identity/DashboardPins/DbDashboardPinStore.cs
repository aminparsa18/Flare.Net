namespace Flare.Identity.DashboardPins;

public sealed class DbDashboardPinStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : IDashboardPinStore
{
    public async Task<IReadOnlyList<Guid>> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT DashboardId FROM DashboardPins WHERE UserId = @userId ORDER BY PinnedAt DESC";
        command.AddParameter("@userId", userId.ToString());

        var ids = new List<Guid>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (Guid.TryParse(reader.GetString(0), out var id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }

    public async Task PinAsync(Guid userId, Guid dashboardId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO DashboardPins (UserId, DashboardId, PinnedAt) VALUES (@userId, @dashboardId, @pinnedAt) ON CONFLICT DO NOTHING";
        command.AddParameter("@userId", userId.ToString());
        command.AddParameter("@dashboardId", dashboardId.ToString());
        command.AddParameter("@pinnedAt", timeProvider.GetUtcNow().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UnpinAsync(Guid userId, Guid dashboardId, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM DashboardPins WHERE UserId = @userId AND DashboardId = @dashboardId";
        command.AddParameter("@userId", userId.ToString());
        command.AddParameter("@dashboardId", dashboardId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
