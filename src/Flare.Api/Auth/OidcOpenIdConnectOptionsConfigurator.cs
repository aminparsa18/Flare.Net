using Flare.Identity.Auth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace Flare.Api.Auth;

/// <summary>
/// Applies the database-backed <see cref="OidcSettings"/> (Authority/ClientId/
/// ClientSecret/Scopes, configured through the dashboard's Admin-only Security screen -
/// see docs/auth.md) to the <c>Oidc</c> <see cref="OpenIdConnectOptions"/> scheme.
/// Directly analogous to <see cref="EntraOpenIdConnectOptionsConfigurator"/> - the only
/// real difference is <see cref="OpenIdConnectOptions.Authority"/> is applied as-is
/// rather than interpolated from a tenant id, since a generic provider isn't tied to
/// Microsoft's URL shape, and <see cref="OpenIdConnectOptions.Scope"/> is populated from
/// the stored space-separated scope list instead of ASP.NET Core's own default.
///
/// Registered via <c>builder.Services.ConfigureOptions&lt;OidcOpenIdConnectOptionsConfigurator&gt;()</c>
/// in <c>Program.cs</c>, alongside the static settings (<c>ResponseType</c>/<c>UsePkce</c>/
/// <c>SignInScheme</c>/<c>CallbackPath</c>) the inline <c>AddOpenIdConnect(...)</c>
/// configure delegate there still sets directly.
///
/// <b>Placeholder values when never configured/disabled</b> - same reasoning
/// <see cref="EntraOpenIdConnectOptionsConfigurator"/> documents in full:
/// <see cref="OpenIdConnectOptions.Validate()"/> unconditionally requires a non-empty
/// <c>Authority</c>/<c>ClientId</c>, and ASP.NET Core's <c>AuthenticationMiddleware</c>
/// resolves (and therefore validates) every registered <c>IAuthenticationRequestHandler</c>
/// scheme - which <c>OpenIdConnectHandler</c> is - on every single request, not only
/// requests that actually challenge it. Falling back to harmless placeholder strings when
/// unconfigured costs nothing: <c>OidcAuthEndpoints</c> gates every real use of this
/// scheme on <see cref="IOidcSettingsStore"/>'s own <c>Enabled</c> flag before ever
/// challenging it, and the provider's metadata document is only ever fetched lazily on an
/// actual authentication attempt.
///
/// Same "settings changes need a restart to take effect" mechanism as Entra's
/// configurator: ASP.NET Core resolves a named options instance lazily, once, then caches
/// it for the process's lifetime - see that class's remarks for the full explanation.
/// </summary>
public sealed class OidcOpenIdConnectOptionsConfigurator(IOidcSettingsStore settingsStore) : IConfigureNamedOptions<OpenIdConnectOptions>
{
    private const string PlaceholderAuthority = "https://not-configured.invalid";
    private const string PlaceholderClientId = "not-configured";

    public void Configure(string? name, OpenIdConnectOptions options)
    {
        if (name != OidcAuthenticationDefaults.SchemeName)
        {
            return;
        }

        var settings = settingsStore.GetAsync().GetAwaiter().GetResult();
        options.Authority = string.IsNullOrEmpty(settings.Authority) ? PlaceholderAuthority : settings.Authority;
        options.ClientId = string.IsNullOrEmpty(settings.ClientId) ? PlaceholderClientId : settings.ClientId;
        options.ClientSecret = string.IsNullOrEmpty(settings.ClientSecret) ? PlaceholderClientId : settings.ClientSecret;

        options.Scope.Clear();
        foreach (var scope in settings.Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            options.Scope.Add(scope);
        }
    }

    // Never actually invoked - Program.cs only ever resolves OpenIdConnectOptions by the
    // "Oidc" name (ASP.NET Core's AddAuthentication/AddOpenIdConnect always names its
    // scheme's options), never through the unnamed IConfigureOptions<T> path. Required by
    // the interface regardless.
    public void Configure(OpenIdConnectOptions options)
    {
    }
}
