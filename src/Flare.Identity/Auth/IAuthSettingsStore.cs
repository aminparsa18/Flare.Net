namespace Flare.Identity.Auth;

public interface IAuthSettingsStore
{
    /// <summary>Never null. The migration seeds exactly one row (backward-compat: true
    /// for any database that already had users, false only for a genuinely fresh
    /// install - see <c>Migrations/0004_auth_settings.sql</c>), so the no-row case below
    /// is defensive only - and deliberately fails toward <c>Enabled: true</c> (the
    /// opposite bias from <see cref="EntraSettings.NotConfigured"/>'s fail-disabled
    /// default), since "unexpectedly open" is the wrong direction to fail toward for the
    /// switch that gates every other endpoint in the app.</summary>
    Task<AuthSettings> GetAsync(CancellationToken cancellationToken = default);

    Task<AuthSettings> SaveAsync(bool enabled, bool localEnabled, CancellationToken cancellationToken = default);
}
