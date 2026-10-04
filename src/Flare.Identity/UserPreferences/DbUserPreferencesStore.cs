namespace Flare.Identity.UserPreferences;

public sealed class DbUserPreferencesStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : IUserPreferencesStore
{
    public async Task<string?> GetAsync(Guid userId, string key, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM UserPreferences WHERE UserId = @userId AND Key = @key";
        command.AddParameter("@userId", userId.ToString());
        command.AddParameter("@key", key);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task SetAsync(Guid userId, string key, string json, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            "INSERT INTO UserPreferences (UserId, Key, Value, UpdatedAt) VALUES (@userId, @key, @value, @updatedAt) " +
            "ON CONFLICT (UserId, Key) DO UPDATE SET Value = excluded.Value, UpdatedAt = excluded.UpdatedAt";
        command.AddParameter("@userId", userId.ToString());
        command.AddParameter("@key", key);
        command.AddParameter("@value", json);
        command.AddParameter("@updatedAt", timeProvider.GetUtcNow().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid userId, string key, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM UserPreferences WHERE UserId = @userId AND Key = @key";
        command.AddParameter("@userId", userId.ToString());
        command.AddParameter("@key", key);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
