using Flare.Identity.Auth;

namespace Flare.Api.Tests.TestSupport;

/// <summary>In-memory <see cref="IEntraSettingsStore"/> - same "pure logic, fake the one
/// interface it depends on" convention as <see cref="FakeUserStore"/>.</summary>
internal sealed class FakeEntraSettingsStore : IEntraSettingsStore
{
    private EntraSettings _settings = EntraSettings.NotConfigured;

    public FakeEntraSettingsStore()
    {
    }

    public FakeEntraSettingsStore(EntraSettings initial)
    {
        _settings = initial;
    }

    public Task<EntraSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(_settings);

    public Task<EntraSettings> SaveAsync(bool enabled, string? tenantId, string? clientId, string? clientSecret, CancellationToken cancellationToken = default)
    {
        _settings = _settings with
        {
            Enabled = enabled,
            TenantId = tenantId,
            ClientId = clientId,
            ClientSecret = clientSecret ?? _settings.ClientSecret,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        return Task.FromResult(_settings);
    }
}
