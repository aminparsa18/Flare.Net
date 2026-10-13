using Flare.Api.Alerting;
using Flare.Identity.Auth;
using Flare.Identity.SourceLinks;

namespace Flare.Api.Auth;

/// <summary>
/// Re-saves identity-store secrets that were written before ADR-0206 so they end up encrypted. Implemented by the
/// decorators below; <see cref="IdentitySecretBackfillService"/> calls each once at startup.
/// </summary>
public interface IIdentitySecretBackfill
{
    /// <summary>Encrypts a stored plaintext secret. Returns how many values were re-saved.</summary>
    Task<int> ProtectLegacySecretsAsync(CancellationToken cancellationToken);
}

/// <summary>Encrypts the Entra client secret at the store boundary (ADR-0206); callers keep seeing plaintext.</summary>
public sealed class ProtectedEntraSettingsStore(IEntraSettingsStore inner, INotificationSecretProtector protector) : IEntraSettingsStore, IIdentitySecretBackfill
{
    public async Task<EntraSettings> GetAsync(CancellationToken cancellationToken = default) => Reveal(await inner.GetAsync(cancellationToken));

    public async Task<EntraSettings> SaveAsync(bool enabled, string? tenantId, string? clientId, string? clientSecret, CancellationToken cancellationToken = default) =>
        Reveal(await inner.SaveAsync(enabled, tenantId, clientId, clientSecret is null ? null : protector.Protect(clientSecret), cancellationToken));

    public async Task<int> ProtectLegacySecretsAsync(CancellationToken cancellationToken)
    {
        var stored = await inner.GetAsync(cancellationToken);
        if (!protector.NeedsProtection(stored.ClientSecret ?? ""))
        {
            return 0;
        }

        await inner.SaveAsync(stored.Enabled, stored.TenantId, stored.ClientId, protector.Protect(stored.ClientSecret!), cancellationToken);
        return 1;
    }

    private EntraSettings Reveal(EntraSettings s) => s with { ClientSecret = s.ClientSecret is null ? null : protector.Unprotect(s.ClientSecret) };
}

/// <summary>Encrypts the OIDC client secret at the store boundary (ADR-0206).</summary>
public sealed class ProtectedOidcSettingsStore(IOidcSettingsStore inner, INotificationSecretProtector protector) : IOidcSettingsStore, IIdentitySecretBackfill
{
    public async Task<OidcSettings> GetAsync(CancellationToken cancellationToken = default) => Reveal(await inner.GetAsync(cancellationToken));

    public async Task<OidcSettings> SaveAsync(
        bool enabled,
        string? displayName,
        string? authority,
        string? clientId,
        string? clientSecret,
        string scopes,
        string roleClaimName,
        Flare.Identity.Users.UserRole defaultRole,
        CancellationToken cancellationToken = default) =>
        Reveal(await inner.SaveAsync(
            enabled, displayName, authority, clientId, clientSecret is null ? null : protector.Protect(clientSecret), scopes, roleClaimName, defaultRole, cancellationToken));

    public async Task<int> ProtectLegacySecretsAsync(CancellationToken cancellationToken)
    {
        var s = await inner.GetAsync(cancellationToken);
        if (!protector.NeedsProtection(s.ClientSecret ?? ""))
        {
            return 0;
        }

        await inner.SaveAsync(s.Enabled, s.DisplayName, s.Authority, s.ClientId, protector.Protect(s.ClientSecret!), s.Scopes, s.RoleClaimName, s.DefaultRole, cancellationToken);
        return 1;
    }

    private OidcSettings Reveal(OidcSettings s) => s with { ClientSecret = s.ClientSecret is null ? null : protector.Unprotect(s.ClientSecret) };
}

/// <summary>Encrypts the LDAP bind password at the store boundary (ADR-0206).</summary>
public sealed class ProtectedLdapSettingsStore(ILdapSettingsStore inner, INotificationSecretProtector protector) : ILdapSettingsStore, IIdentitySecretBackfill
{
    public async Task<LdapSettings> GetAsync(CancellationToken cancellationToken = default) => Reveal(await inner.GetAsync(cancellationToken));

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
        Flare.Identity.Users.UserRole defaultRole,
        CancellationToken cancellationToken = default) =>
        Reveal(await inner.SaveAsync(
            enabled, host, port, useSsl, pinnedCertificatePem, baseDn, bindDn, bindPassword is null ? null : protector.Protect(bindPassword),
            userSearchFilter, uniqueIdAttribute, adminGroupDn, memberGroupDn, viewerGroupDn, defaultRole, cancellationToken));

    public async Task<int> ProtectLegacySecretsAsync(CancellationToken cancellationToken)
    {
        var s = await inner.GetAsync(cancellationToken);
        if (!protector.NeedsProtection(s.BindPassword ?? ""))
        {
            return 0;
        }

        await inner.SaveAsync(
            s.Enabled, s.Host, s.Port, s.UseSsl, s.PinnedCertificatePem, s.BaseDn, s.BindDn, protector.Protect(s.BindPassword!),
            s.UserSearchFilter, s.UniqueIdAttribute, s.AdminGroupDn, s.MemberGroupDn, s.ViewerGroupDn, s.DefaultRole, cancellationToken);
        return 1;
    }

    private LdapSettings Reveal(LdapSettings s) => s with { BindPassword = s.BindPassword is null ? null : protector.Unprotect(s.BindPassword) };
}

/// <summary>Encrypts per-service source-repo access tokens at the store boundary (ADR-0206).</summary>
public sealed class ProtectedSourceLinkStore(ISourceLinkStore inner, INotificationSecretProtector protector) : ISourceLinkStore, IIdentitySecretBackfill
{
    public async Task<IReadOnlyList<SourceLinkConfig>> GetAllAsync(CancellationToken cancellationToken = default) =>
        [.. (await inner.GetAllAsync(cancellationToken)).Select(Reveal)];

    // null keeps the stored token and "" clears it; Protect leaves both alone.
    public Task SetAsync(SourceLinkConfig config, CancellationToken cancellationToken = default) =>
        inner.SetAsync(config with { AccessToken = config.AccessToken is null ? null : protector.Protect(config.AccessToken) }, cancellationToken);

    public Task DeleteAsync(string serviceName, CancellationToken cancellationToken = default) => inner.DeleteAsync(serviceName, cancellationToken);

    public async Task<int> ProtectLegacySecretsAsync(CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var link in await inner.GetAllAsync(cancellationToken))
        {
            if (protector.NeedsProtection(link.AccessToken ?? ""))
            {
                await inner.SetAsync(link with { AccessToken = protector.Protect(link.AccessToken!) }, cancellationToken);
                count++;
            }
        }

        return count;
    }

    private SourceLinkConfig Reveal(SourceLinkConfig c) => c with { AccessToken = c.AccessToken is null ? null : protector.Unprotect(c.AccessToken) };
}

/// <summary>
/// Encrypts identity-store secrets saved before ADR-0206. Runs once at startup, is idempotent, and only logs on failure:
/// the rows keep working as plaintext and the next start tries again.
/// </summary>
public sealed class IdentitySecretBackfillService(IEnumerable<IIdentitySecretBackfill> stores, ILogger<IdentitySecretBackfillService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var total = 0;
            foreach (var store in stores)
            {
                total += await store.ProtectLegacySecretsAsync(stoppingToken);
            }

            if (total > 0)
            {
                logger.LogInformation("Encrypted {Count} stored identity secret(s) (SSO/LDAP/source-link)", total);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not encrypt legacy identity secrets; will retry on next start");
        }
    }
}
