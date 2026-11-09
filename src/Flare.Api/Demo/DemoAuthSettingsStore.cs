using Flare.Identity.Auth;

namespace Flare.Api.Demo;

/// <summary>
/// Pins auth on in demo mode (ADR-0278): every reader (the conditional authorization handler,
/// <c>/api/auth/bootstrap/status</c>, the login endpoints) sees auth and local login enabled,
/// whatever is stored, so an Admin can't open the instance to everyone or lock themselves out of
/// local login from the dashboard.
/// </summary>
public sealed class DemoAuthSettingsStore(IAuthSettingsStore inner) : IAuthSettingsStore
{
    public async Task<AuthSettings> GetAsync(CancellationToken cancellationToken = default) =>
        (await inner.GetAsync(cancellationToken)) with { Enabled = true, LocalEnabled = true };

    public async Task<AuthSettings> SaveAsync(bool enabled, bool localEnabled, CancellationToken cancellationToken = default) =>
        (await inner.SaveAsync(enabled: true, localEnabled: true, cancellationToken)) with { Enabled = true, LocalEnabled = true };
}
