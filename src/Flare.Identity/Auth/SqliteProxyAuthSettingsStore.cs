using Flare.Identity.Users;
using Microsoft.Data.Sqlite;

namespace Flare.Identity.Auth;

public sealed class SqliteProxyAuthSettingsStore(IdentityDbConnectionFactory connectionFactory, TimeProvider timeProvider) : IProxyAuthSettingsStore
{
    private const string Columns = "Enabled, HeaderName, TrustedProxyCidrs, GroupsHeaderName, AdminGroup, MemberGroup, ViewerGroup, DefaultRole, UpdatedAt, LogoutRedirectUrl";

    public async Task<ProxyAuthSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {Columns} FROM ProxyAuthSettings WHERE Id = 1";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadSettings(reader) : ProxyAuthSettings.NotConfigured;
    }

    public async Task<ProxyAuthSettings> SaveAsync(
        bool enabled,
        string headerName,
        string trustedProxyCidrs,
        string? groupsHeaderName,
        string? adminGroup,
        string? memberGroup,
        string? viewerGroup,
        UserRole defaultRole,
        string? logoutRedirectUrl,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await connectionFactory.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        // Upsert keyed on the fixed Id=1, same singleton shape as every other settings
        // store - no COALESCE-on-null trick needed here, unlike Entra/LDAP/OIDC's secret
        // fields, since nothing in this record is ever masked/left-unchanged.
        command.CommandText =
            $"""
             INSERT INTO ProxyAuthSettings (Id, {Columns})
             VALUES (1, $enabled, $headerName, $trustedProxyCidrs, $groupsHeaderName, $adminGroup, $memberGroup, $viewerGroup, $defaultRole, $updatedAt, $logoutRedirectUrl)
             ON CONFLICT(Id) DO UPDATE SET
                 Enabled = excluded.Enabled,
                 HeaderName = excluded.HeaderName,
                 TrustedProxyCidrs = excluded.TrustedProxyCidrs,
                 GroupsHeaderName = excluded.GroupsHeaderName,
                 AdminGroup = excluded.AdminGroup,
                 MemberGroup = excluded.MemberGroup,
                 ViewerGroup = excluded.ViewerGroup,
                 DefaultRole = excluded.DefaultRole,
                 UpdatedAt = excluded.UpdatedAt,
                 LogoutRedirectUrl = excluded.LogoutRedirectUrl
             RETURNING {Columns}
             """;
        command.Parameters.AddWithValue("$enabled", enabled ? 1 : 0);
        command.Parameters.AddWithValue("$headerName", headerName);
        command.Parameters.AddWithValue("$trustedProxyCidrs", trustedProxyCidrs);
        command.Parameters.AddWithValue("$groupsHeaderName", (object?)groupsHeaderName ?? DBNull.Value);
        command.Parameters.AddWithValue("$adminGroup", (object?)adminGroup ?? DBNull.Value);
        command.Parameters.AddWithValue("$memberGroup", (object?)memberGroup ?? DBNull.Value);
        command.Parameters.AddWithValue("$viewerGroup", (object?)viewerGroup ?? DBNull.Value);
        command.Parameters.AddWithValue("$defaultRole", defaultRole.ToString());
        command.Parameters.AddWithValue("$updatedAt", timeProvider.GetUtcNow().ToString("O"));
        command.Parameters.AddWithValue("$logoutRedirectUrl", (object?)logoutRedirectUrl ?? DBNull.Value);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return ReadSettings(reader);
    }

    private static ProxyAuthSettings ReadSettings(SqliteDataReader reader) => new(
        Enabled: reader.GetInt64(0) != 0,
        HeaderName: reader.GetString(1),
        TrustedProxyCidrs: reader.GetString(2),
        GroupsHeaderName: reader.IsDBNull(3) ? null : reader.GetString(3),
        AdminGroup: reader.IsDBNull(4) ? null : reader.GetString(4),
        MemberGroup: reader.IsDBNull(5) ? null : reader.GetString(5),
        ViewerGroup: reader.IsDBNull(6) ? null : reader.GetString(6),
        DefaultRole: Enum.Parse<UserRole>(reader.GetString(7)),
        UpdatedAt: reader.IsDBNull(8) ? null : DateTimeOffset.Parse(reader.GetString(8)),
        LogoutRedirectUrl: reader.IsDBNull(9) ? null : reader.GetString(9));
}
