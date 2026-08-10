using Flare.Api.Auth;
using Flare.Api.Tests.TestSupport;
using Flare.Identity.Auth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Xunit;

namespace Flare.Api.Tests.Auth;

/// <summary>
/// Regression coverage for a real bug a live <c>docker compose</c> run caught: with an
/// unconfigured <see cref="EntraSettings"/> row, <see cref="OpenIdConnectOptions.Validate"/>
/// (triggered on *every* request, not just Entra ones - see
/// <see cref="EntraOpenIdConnectOptionsConfigurator"/>'s remarks) threw
/// <c>ArgumentNullException: ClientId</c>, 500ing the whole API. These tests assert the
/// configurator never leaves <c>Authority</c>/<c>ClientId</c>/<c>ClientSecret</c> null or
/// empty, configured or not - the actual condition <c>Validate()</c> failed on. They
/// don't call <c>Validate()</c> directly: the real pipeline runs ASP.NET Core's internal
/// <c>OpenIdConnectPostConfigureOptions.PostConfigure</c> (which builds
/// <c>ConfigurationManager</c> from <c>Authority</c>) *before* <c>Validate()</c>, a step
/// this test can't reach (it's `internal`) - the live `docker compose` run, not this
/// unit test, is what confirmed the full pipeline no longer crashes.
/// </summary>
public class EntraOpenIdConnectOptionsConfiguratorTests
{
    [Fact]
    public void Configure_ProducesNonEmptyValues_WhenNeverConfigured()
    {
        var configurator = new EntraOpenIdConnectOptionsConfigurator(new FakeEntraSettingsStore());
        var options = new OpenIdConnectOptions { SignInScheme = "EntraExternal" };

        configurator.Configure(EntraAuthenticationDefaults.SchemeName, options);

        Assert.False(string.IsNullOrEmpty(options.Authority));
        Assert.False(string.IsNullOrEmpty(options.ClientId));
        Assert.False(string.IsNullOrEmpty(options.ClientSecret));
    }

    [Fact]
    public void Configure_AppliesTheStoredSettings_WhenConfigured()
    {
        var store = new FakeEntraSettingsStore(new EntraSettings(true, "my-tenant", "my-client", "my-secret", DateTimeOffset.UtcNow));
        var configurator = new EntraOpenIdConnectOptionsConfigurator(store);
        var options = new OpenIdConnectOptions { SignInScheme = "EntraExternal" };

        configurator.Configure(EntraAuthenticationDefaults.SchemeName, options);

        Assert.Equal("https://login.microsoftonline.com/my-tenant/v2.0", options.Authority);
        Assert.Equal("my-client", options.ClientId);
        Assert.Equal("my-secret", options.ClientSecret);
    }

    [Fact]
    public void Configure_IgnoresOtherSchemeNames()
    {
        var configurator = new EntraOpenIdConnectOptionsConfigurator(new FakeEntraSettingsStore(new EntraSettings(true, "my-tenant", "my-client", "my-secret", DateTimeOffset.UtcNow)));
        var options = new OpenIdConnectOptions();

        configurator.Configure("SomeOtherScheme", options);

        Assert.Null(options.ClientId);
    }
}
