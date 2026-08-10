using Flare.Identity.Auth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Flare.Api.Auth;

/// <summary>
/// Applies the database-backed <see cref="EntraSettings"/> (Authority/ClientId/
/// ClientSecret, configured through the dashboard's Admin-only Security screen - see
/// docs/auth.md) to the <c>Entra</c> <see cref="OpenIdConnectOptions"/> scheme.
///
/// Registered via <c>builder.Services.ConfigureOptions&lt;EntraOpenIdConnectOptionsConfigurator&gt;()</c>
/// in <c>Program.cs</c>, alongside the static settings (<c>ResponseType</c>/<c>UsePkce</c>/
/// <c>SignInScheme</c>/claim-type mapping) the inline <c>AddOpenIdConnect(...)</c>
/// configure delegate there still sets directly - ASP.NET Core's options system runs
/// every registered <see cref="IConfigureOptions{TOptions}"/>/<see cref="IConfigureNamedOptions{TOptions}"/>
/// for a given options type, in registration order, so both apply to the same
/// <see cref="OpenIdConnectOptions"/> instance.
///
/// <b>Placeholder values when never configured/disabled, found the hard way:</b>
/// <see cref="OpenIdConnectOptions.Validate()"/> unconditionally requires a non-empty
/// <c>Authority</c>/<c>ClientId</c> - and ASP.NET Core's <c>AuthenticationMiddleware</c>
/// resolves (and therefore validates) *every* registered scheme that implements
/// <c>IAuthenticationRequestHandler</c> - which <c>OpenIdConnectHandler</c> does - on
/// *every single request*, to check whether that request's path matches the scheme's
/// own <c>CallbackPath</c>, not only on requests that actually challenge it. A real,
/// live `docker compose` run confirmed this the hard way: with an unconfigured
/// <c>EntraSettings</c> row, every request (including <c>/health</c>) 500'd with
/// <c>ArgumentNullException: ClientId</c>, before this fix. Falling back to harmless
/// placeholder strings when unconfigured fixes it correctly, not just quietly: these
/// values are never actually used for anything real - <c>EntraAuthEndpoints</c> gates
/// every real use of this scheme on <see cref="IEntraSettingsStore"/>'s own
/// <c>Enabled</c> flag before ever challenging it, and Microsoft's metadata document is
/// only ever fetched lazily on an actual authentication attempt, never at
/// options-configure time - so a syntactically-valid-but-fake placeholder costs nothing
/// and touches no network.
///
/// This is also the entire mechanism behind "settings changes need a restart to take
/// effect" (a deliberate choice - see the plan this shipped from): ASP.NET Core resolves
/// a named options instance lazily, once, then caches it for the process's lifetime
/// unless something explicitly invalidates it. Nothing here ever does, so
/// <see cref="Configure(string?, OpenIdConnectOptions)"/> only actually runs once, on
/// this scheme's first resolution (in practice, the very first incoming request after a
/// process start, per the paragraph above) - by then
/// <c>IdentityMigrationRunner.ApplyAsync</c> (called right after <c>builder.Build()</c>
/// in <c>Program.cs</c>) has long since created the <c>EntraSettings</c> table, so
/// blocking on <see cref="IEntraSettingsStore.GetAsync"/> here (this callback has no
/// async-capable signature - standard for one-time startup-shaped option configuration)
/// is safe.
/// </summary>
public sealed class EntraOpenIdConnectOptionsConfigurator(IEntraSettingsStore settingsStore) : IConfigureNamedOptions<OpenIdConnectOptions>
{
    private const string PlaceholderTenantId = "common";
    private const string PlaceholderClientId = "not-configured";

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name != EntraAuthenticationDefaults.SchemeName)
        {
            return;
        }

        var settings = settingsStore.GetAsync().GetAwaiter().GetResult();
        var tenantId = string.IsNullOrEmpty(settings.TenantId) ? PlaceholderTenantId : settings.TenantId;
        options.Authority = $"https://login.microsoftonline.com/{tenantId}/v2.0";
        options.ClientId = string.IsNullOrEmpty(settings.ClientId) ? PlaceholderClientId : settings.ClientId;
        options.ClientSecret = string.IsNullOrEmpty(settings.ClientSecret) ? PlaceholderClientId : settings.ClientSecret;
    }

    // Never actually invoked - Program.cs only ever resolves OpenIdConnectOptions by the
    // "Entra" name (ASP.NET Core's AddAuthentication/AddOpenIdConnect always names its
    // scheme's options), never through the unnamed IConfigureOptions<T> path. Required
    // by the interface regardless.
    public void Configure(OpenIdConnectOptions options)
    {
    }
}
