using Flare.Identity.Users;
using Microsoft.Data.Sqlite;

namespace Flare.Identity.Auth;

public sealed class SqliteLdapSettingsStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : ILdapSettingsStore
{
    private const string Columns =
        "Enabled, Host, Port, UseSsl, BaseDn, BindDn, BindPassword, UserSearchFilter, UniqueIdAttribute, AdminGroupDn, MemberGroupDn, ViewerGroupDn, DefaultRole, UpdatedAt, PinnedCertificatePem";

    public async Task<LdapSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM LdapSettings WHERE Id = 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSettings(reader) : LdapSettings.NotConfigured;
    }

    public async Task<LdapSettings> SaveAsync(
        bool enabled,
        string? host,
        int port,
        bool useSsl,
        string? pinnedCertificatePem,
        string? baseDn,
        string? bindDn,
        string? bindPassword,
        string userSearchFilter,
        string uniqueIdAttribute,
        string? adminGroupDn,
        string? memberGroupDn,
        string? viewerGroupDn,
        UserRole defaultRole,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Upsert keyed on the fixed Id=1, same COALESCE-on-null trick as
        // SqliteEntraSettingsStore.SaveAsync for BindPassword specifically - every other
        // field, including PinnedCertificatePem, is always supplied together by the
        // dashboard's save button, so a null there is a deliberate clear, not "unchanged"
        // (see ILdapSettingsStore.SaveAsync's remarks).
        command.CommandText =
            $"""
             INSERT INTO LdapSettings (Id, {Columns})
             VALUES (1, $enabled, $host, $port, $useSsl, $baseDn, $bindDn, $bindPassword, $userSearchFilter, $uniqueIdAttribute, $adminGroupDn, $memberGroupDn, $viewerGroupDn, $defaultRole, $updatedAt, $pinnedCertificatePem)
             ON CONFLICT(Id) DO UPDATE SET
                 Enabled = excluded.Enabled,
                 Host = excluded.Host,
                 Port = excluded.Port,
                 UseSsl = excluded.UseSsl,
                 BaseDn = excluded.BaseDn,
                 BindDn = excluded.BindDn,
                 BindPassword = COALESCE(excluded.BindPassword, LdapSettings.BindPassword),
                 UserSearchFilter = excluded.UserSearchFilter,
                 UniqueIdAttribute = excluded.UniqueIdAttribute,
                 AdminGroupDn = excluded.AdminGroupDn,
                 MemberGroupDn = excluded.MemberGroupDn,
                 ViewerGroupDn = excluded.ViewerGroupDn,
                 DefaultRole = excluded.DefaultRole,
                 UpdatedAt = excluded.UpdatedAt,
                 PinnedCertificatePem = excluded.PinnedCertificatePem
             RETURNING {Columns}
             """;
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$host", (object?)host ?? DBNull.Value);
        command.Parameters.AddWithValue("$port", port);
        command.Parameters.AddWithValue("$useSsl", useSsl ? 1 : 0);
        command.Parameters.AddWithValue("$baseDn", (object?)baseDn ?? DBNull.Value);
        command.Parameters.AddWithValue("$bindDn", (object?)bindDn ?? DBNull.Value);
        command.Parameters.AddWithValue("$bindPassword", (object?)bindPassword ?? DBNull.Value);
        command.Parameters.AddWithValue("$userSearchFilter", userSearchFilter);
        command.Parameters.AddWithValue("$uniqueIdAttribute", uniqueIdAttribute);
        command.Parameters.AddWithValue("$adminGroupDn", (object?)adminGroupDn ?? DBNull.Value);
        command.Parameters.AddWithValue("$memberGroupDn", (object?)memberGroupDn ?? DBNull.Value);
        command.Parameters.AddWithValue("$viewerGroupDn", (object?)viewerGroupDn ?? DBNull.Value);
        command.Parameters.AddWithValue("$defaultRole", defaultRole.ToString());
        command.Parameters.AddWithValue("$updatedAt", timeProvider.GetUtcNow().ToString("O"));
        command.Parameters.AddWithValue("$pinnedCertificatePem", (object?)pinnedCertificatePem ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ReadSettings(reader);
    }

    private static LdapSettings ReadSettings(SqliteDataReader reader) => new(
        Enabled: reader.GetInt64(0) != 0,
        Host: reader.IsDBNull(1) ? null : reader.GetString(1),
        Port: (int)reader.GetInt64(2),
        UseSsl: reader.GetInt64(3) != 0,
        BaseDn: reader.IsDBNull(4) ? null : reader.GetString(4),
        BindDn: reader.IsDBNull(5) ? null : reader.GetString(5),
        BindPassword: reader.IsDBNull(6) ? null : reader.GetString(6),
        UserSearchFilter: reader.GetString(7),
        UniqueIdAttribute: reader.GetString(8),
        AdminGroupDn: reader.IsDBNull(9) ? null : reader.GetString(9),
        MemberGroupDn: reader.IsDBNull(10) ? null : reader.GetString(10),
        ViewerGroupDn: reader.IsDBNull(11) ? null : reader.GetString(11),
        DefaultRole: Enum.Parse<UserRole>(reader.GetString(12)),
        UpdatedAt: reader.IsDBNull(13) ? null : DateTimeOffset.Parse(reader.GetString(13)),
        PinnedCertificatePem: reader.IsDBNull(14) ? null : reader.GetString(14));
}
