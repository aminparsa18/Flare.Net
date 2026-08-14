using Microsoft.Data.Sqlite;

namespace Flare.Identity.Auth;

public sealed class SqliteAuthSettingsStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : IAuthSettingsStore
{
    public async Task<AuthSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Enabled, LocalEnabled, UpdatedAt FROM AuthSettings WHERE Id = 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        // Fail-secure default (see IAuthSettingsStore's remarks) - the migration always
        // seeds a row, so this is defensive only, never the real path in practice.
        return await reader.ReadAsync(cancellationToken)
            ? ReadSettings(reader)
            : new AuthSettings(Enabled: true, LocalEnabled: true, UpdatedAt: null);
    }

    public async Task<AuthSettings> SaveAsync(bool enabled, bool localEnabled, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Upsert keyed on the fixed Id=1, same shape as SqliteEntraSettingsStore.SaveAsync -
        // no COALESCE-on-null trick needed here, both fields are always supplied together
        // (unlike a write-once secret).
        command.CommandText =
            """
            INSERT INTO AuthSettings (Id, Enabled, LocalEnabled, UpdatedAt)
            VALUES (1, $enabled, $localEnabled, $updatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                Enabled = excluded.Enabled,
                LocalEnabled = excluded.LocalEnabled,
                UpdatedAt = excluded.UpdatedAt
            RETURNING Enabled, LocalEnabled, UpdatedAt
            """;
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$localEnabled", localEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", timeProvider.GetUtcNow().ToString("O"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ReadSettings(reader);
    }

    private static AuthSettings ReadSettings(SqliteDataReader reader) => new(
        Enabled: reader.GetInt64(0) != 0,
        LocalEnabled: reader.GetInt64(1) != 0,
        UpdatedAt: reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)));
}
