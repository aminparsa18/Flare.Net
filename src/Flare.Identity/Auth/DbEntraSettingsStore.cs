using System.Data.Common;

namespace Flare.Identity.Auth;

public sealed class DbEntraSettingsStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : IEntraSettingsStore
{
    public async Task<EntraSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT Enabled, TenantId, ClientId, ClientSecret, UpdatedAt FROM EntraSettings WHERE Id = 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSettings(reader) : EntraSettings.NotConfigured;
    }

    public async Task<EntraSettings> SaveAsync(bool enabled, string? tenantId, string? clientId, string? clientSecret, CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Upsert keyed on the fixed Id=1 (see the migration's "settings singleton"
        // remarks). ClientSecret only overwrites the stored value when the caller
        // actually provided one - a null @clientSecret coalesces back to whatever
        // EntraSettings.ClientSecret (the *existing* row's value, not the just-inserted
        // one - "excluded" is SQLite's name for the row that would have been inserted)
        // already held, so the dashboard's blank-means-unchanged field never needs a
        // separate read-before-write. RETURNING avoids that same separate read for the
        // response this method hands back.
        command.CommandText =
            """
            INSERT INTO EntraSettings (Id, Enabled, TenantId, ClientId, ClientSecret, UpdatedAt)
            VALUES (1, @enabled, @tenantId, @clientId, @clientSecret, @updatedAt)
            ON CONFLICT(Id) DO UPDATE SET
                Enabled = excluded.Enabled,
                TenantId = excluded.TenantId,
                ClientId = excluded.ClientId,
                ClientSecret = COALESCE(excluded.ClientSecret, EntraSettings.ClientSecret),
                UpdatedAt = excluded.UpdatedAt
            RETURNING Enabled, TenantId, ClientId, ClientSecret, UpdatedAt
            """;
        command.AddParameter("@enabled", enabled ? 1 : 0);
        command.AddParameter("@tenantId", (object?)tenantId ?? DBNull.Value);
        command.AddParameter("@clientId", (object?)clientId ?? DBNull.Value);
        command.AddParameter("@clientSecret", (object?)clientSecret ?? DBNull.Value);
        command.AddParameter("@updatedAt", timeProvider.GetUtcNow().ToString("O"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ReadSettings(reader);
    }

    private static EntraSettings ReadSettings(DbDataReader reader) => new(
        Enabled: reader.GetInt64(0) != 0,
        TenantId: reader.IsDBNull(1) ? null : reader.GetString(1),
        ClientId: reader.IsDBNull(2) ? null : reader.GetString(2),
        ClientSecret: reader.IsDBNull(3) ? null : reader.GetString(3),
        UpdatedAt: reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4)));
}
