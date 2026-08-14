using Flare.Identity.Auth;

namespace Flare.Api.Tests.TestSupport;

/// <summary>In-memory <see cref="IAuthSettingsStore"/> - same convention as
/// <see cref="FakeEntraSettingsStore"/>.</summary>
internal sealed class FakeAuthSettingsStore(bool enabled = true, bool localEnabled = true) : IAuthSettingsStore
{
    private AuthSettings _settings = new(enabled, localEnabled, DateTimeOffset.UtcNow);

    public Task<AuthSettings> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(_settings);

    public Task<AuthSettings> SaveAsync(bool enabled, bool localEnabled, CancellationToken cancellationToken = default)
    {
        _settings = new AuthSettings(enabled, localEnabled, DateTimeOffset.UtcNow);
        return Task.FromResult(_settings);
    }
}
