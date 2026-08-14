using Flare.Api.Auth;
using Flare.Api.Tests.TestSupport;
using Flare.Identity.Auth;
using Flare.Identity.Users;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Xunit;

namespace Flare.Api.Tests.Auth;

/// <summary>
/// Same regression coverage as <see cref="EntraOpenIdConnectOptionsConfiguratorTests"/> -
/// asserts <see cref="OidcOpenIdConnectOptionsConfigurator"/> never leaves
/// <c>Authority</c>/<c>ClientId</c>/<c>ClientSecret</c> null or empty, configured or not
/// (the condition that crashed every request when Entra's own configurator didn't do
/// this - see that class's remarks), plus that the stored space-separated
/// <see cref="OidcSettings.Scopes"/> string is applied to <c>Scope</c> correctly.
/// </summary>
public class OidcOpenIdConnectOptionsConfiguratorTests
{
    [Fact]
    public void Configure_ProducesNonEmptyValues_WhenNeverConfigured()
    {
        var configurator = new OidcOpenIdConnectOptionsConfigurator(new FakeOidcSettingsStore());
        var options = new OpenIdConnectOptions { SignInScheme = "OidcExternal" };

        configurator.Configure(OidcAuthenticationDefaults.SchemeName, options);

        Assert.False(string.IsNullOrEmpty(options.Authority));
        Assert.False(string.IsNullOrEmpty(options.ClientId));
        Assert.False(string.IsNullOrEmpty(options.ClientSecret));
    }

    [Fact]
    public void Configure_AppliesTheStoredSettings_WhenConfigured()
    {
        var store = new FakeOidcSettingsStore(new OidcSettings(
            true, "Okta", "https://example.okta.com", "my-client", "my-secret", "openid profile email groups", "roles", UserRole.Viewer, DateTimeOffset.UtcNow));
        var configurator = new OidcOpenIdConnectOptionsConfigurator(store);
        var options = new OpenIdConnectOptions { SignInScheme = "OidcExternal" };

        configurator.Configure(OidcAuthenticationDefaults.SchemeName, options);

        Assert.Equal("https://example.okta.com", options.Authority);
        Assert.Equal("my-client", options.ClientId);
        Assert.Equal("my-secret", options.ClientSecret);
        Assert.Equal(["openid", "profile", "email", "groups"], options.Scope);
    }

    [Fact]
    public void Configure_IgnoresOtherSchemeNames()
    {
        var configurator = new OidcOpenIdConnectOptionsConfigurator(new FakeOidcSettingsStore(new OidcSettings(
            true, "Okta", "https://example.okta.com", "my-client", "my-secret", "openid profile email", "roles", UserRole.Viewer, DateTimeOffset.UtcNow)));
        var options = new OpenIdConnectOptions();

        configurator.Configure("SomeOtherScheme", options);

        Assert.Null(options.ClientId);
    }
}
