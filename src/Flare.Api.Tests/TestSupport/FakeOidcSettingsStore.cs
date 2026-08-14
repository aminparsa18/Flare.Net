using Flare.Identity.Auth;
using Flare.Identity.Users;

namespace Flare.Api.Tests.TestSupport;

/// <summary>In-memory <see cref="IOidcSettingsStore"/> - same convention as
/// <see cref="FakeEntraSettingsStore"/>/<see cref="FakeLdapSettingsStore"/>.</summary>
internal sealed class FakeOidcSettingsStore : IOidcSettingsStore
{
    private OidcSettings _settings = OidcSettings.NotConfigured;

    public FakeOidcSettingsStore()
    {
    }

    public FakeOidcSettingsStore(OidcSettings initial)
    {
        _settings = initial;
    }

    public Task<OidcSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(_settings);

    public Task<OidcSettings> SaveAsync(
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
        _settings = _settings with
        {
            Enabled = enabled,
            DisplayName = displayName,
            Authority = authority,
            ClientId = clientId,
            ClientSecret = clientSecret ?? _settings.ClientSecret,
            Scopes = scopes,
            RoleClaimName = roleClaimName,
            DefaultRole = defaultRole,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        return Task.FromResult(_settings);
    }
}
