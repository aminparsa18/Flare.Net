using Flare.Identity.Users;
using Microsoft.Data.Sqlite;

namespace Flare.Identity.Auth;

public sealed class SqliteOidcSettingsStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : IOidcSettingsStore
{
    private const string Columns = "Enabled, DisplayName, Authority, ClientId, ClientSecret, Scopes, RoleClaimName, DefaultRole, UpdatedAt";

    public async Task<OidcSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM OidcSettings WHERE Id = 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSettings(reader) : OidcSettings.NotConfigured;
    }

    public async Task<OidcSettings> SaveAsync(
        bool enabled,
        string? displayName,
        string? authority,
        string? clientId,
        string? clientSecret,
        string scopes,
        string roleClaimName,
        UserRole defaultRole,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Upsert keyed on the fixed Id=1, same COALESCE-on-null trick as
        // SqliteEntraSettingsStore.SaveAsync for ClientSecret specifically - every other
        // field is always supplied together by the dashboard's save button.
        command.CommandText =
            $"""
             INSERT INTO OidcSettings (Id, {Columns})
             VALUES (1, $enabled, $displayName, $authority, $clientId, $clientSecret, $scopes, $roleClaimName, $defaultRole, $updatedAt)
             ON CONFLICT(Id) DO UPDATE SET
                 Enabled = excluded.Enabled,
                 DisplayName = excluded.DisplayName,
                 Authority = excluded.Authority,
                 ClientId = excluded.ClientId,
                 ClientSecret = COALESCE(excluded.ClientSecret, OidcSettings.ClientSecret),
                 Scopes = excluded.Scopes,
                 RoleClaimName = excluded.RoleClaimName,
                 DefaultRole = excluded.DefaultRole,
                 UpdatedAt = excluded.UpdatedAt
             RETURNING {Columns}
             """;
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$displayName", (object?)displayName ?? DBNull.Value);
        command.Parameters.AddWithValue("$authority", (object?)authority ?? DBNull.Value);
        command.Parameters.AddWithValue("$clientId", (object?)clientId ?? DBNull.Value);
        command.Parameters.AddWithValue("$clientSecret", (object?)clientSecret ?? DBNull.Value);
        command.Parameters.AddWithValue("$scopes", scopes);
        command.Parameters.AddWithValue("$roleClaimName", roleClaimName);
        command.Parameters.AddWithValue("$defaultRole", defaultRole.ToString());
        command.Parameters.AddWithValue("$updatedAt", timeProvider.GetUtcNow().ToString("O"));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ReadSettings(reader);
    }

    private static OidcSettings ReadSettings(SqliteDataReader reader) => new(
        Enabled: reader.GetInt64(0) != 0,
        DisplayName: reader.IsDBNull(1) ? null : reader.GetString(1),
        Authority: reader.IsDBNull(2) ? null : reader.GetString(2),
        ClientId: reader.IsDBNull(3) ? null : reader.GetString(3),
        ClientSecret: reader.IsDBNull(4) ? null : reader.GetString(4),
        Scopes: reader.GetString(5),
        RoleClaimName: reader.GetString(6),
        DefaultRole: Enum.Parse<UserRole>(reader.GetString(7)),
        UpdatedAt: reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)));
}
