using System.Text;
using System.Text.Json;
using Flare.Api.Endpoints;
using Flare.Api.Json;
using Flare.Api.Tests.TestSupport;
using Flare.Identity.Auth;
using Flare.Identity.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Api.Tests.Endpoints;

/// <summary>
/// Exercises <see cref="OidcSettingsEndpoints"/>' handlers directly against
/// <see cref="FakeOidcSettingsStore"/>, same convention as
/// <see cref="EntraSettingsEndpointsTests"/>.
/// </summary>
public class OidcSettingsEndpointsTests
{
    private static readonly IServiceProvider EmptyRequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();

    private static DefaultHttpContext CreateContext(object? jsonBody = null)
    {
        var context = new DefaultHttpContext { RequestServices = EmptyRequestServices };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("flare.example.com");
        if (jsonBody is not null)
        {
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(jsonBody)));
        }
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<T?> ReadJsonBodyAsync<T>(DefaultHttpContext context, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        context.Response.Body.Position = 0;
        return await JsonSerializer.DeserializeAsync(context.Response.Body, typeInfo);
    }

    [Fact]
    public async Task Get_ReportsNotConfigured_WhenNoSettingsSavedYet()
    {
        var context = CreateContext();

        var result = await OidcSettingsEndpoints.HandleGetAsync(context, new FakeOidcSettingsStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        var dto = await ReadJsonBodyAsync(context, OidcSettingsJsonContext.Default.OidcSettingsDto);
        Assert.False(dto!.Enabled);
        Assert.False(dto.HasClientSecret);
        Assert.Equal("https://flare.example.com/signin-oidc-generic", dto.RedirectUri);
        Assert.Equal("openid profile email", dto.Scopes);
        Assert.Equal("roles", dto.RoleClaimName);
    }

    [Fact]
    public async Task Get_NeverReturnsTheRealClientSecret()
    {
        var store = new FakeOidcSettingsStore(new OidcSettings(
            true, "Okta", "https://example.okta.com", "client-1", "super-secret-value", "openid profile email", "roles", UserRole.Viewer, DateTimeOffset.UtcNow));
        var context = CreateContext();

        var result = await OidcSettingsEndpoints.HandleGetAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        var raw = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("super-secret-value", raw);

        var dto = await ReadJsonBodyAsync(context, OidcSettingsJsonContext.Default.OidcSettingsDto);
        Assert.True(dto!.HasClientSecret);
    }

    [Fact]
    public async Task Put_PersistsAuthorityAndClientId()
    {
        var store = new FakeOidcSettingsStore();
        var context = CreateContext(new
        {
            enabled = true,
            displayName = "Okta",
            authority = "https://example.okta.com",
            clientId = "client-1",
            clientSecret = "secret-1",
            scopes = "openid profile email",
            roleClaimName = "roles",
            defaultRole = "Viewer",
        });

        var result = await OidcSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        var saved = await store.GetAsync();
        Assert.True(saved.Enabled);
        Assert.Equal("Okta", saved.DisplayName);
        Assert.Equal("https://example.okta.com", saved.Authority);
        Assert.Equal("client-1", saved.ClientId);
        Assert.Equal("secret-1", saved.ClientSecret);
    }

    [Fact]
    public async Task Put_WithNullClientSecret_PreservesTheExistingOne()
    {
        var store = new FakeOidcSettingsStore(new OidcSettings(
            true, "Okta", "https://example.okta.com", "client-1", "original-secret", "openid profile email", "roles", UserRole.Viewer, DateTimeOffset.UtcNow));
        var context = CreateContext(new
        {
            enabled = true,
            displayName = "Okta",
            authority = "https://example2.okta.com",
            clientId = "client-1",
            clientSecret = (string?)null,
            scopes = "openid profile email",
            roleClaimName = "roles",
            defaultRole = "Viewer",
        });

        var result = await OidcSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        var saved = await store.GetAsync();
        Assert.Equal("https://example2.okta.com", saved.Authority);
        Assert.Equal("original-secret", saved.ClientSecret);
    }

    [Fact]
    public async Task Put_Returns400_WhenEnablingWithoutAnAuthority()
    {
        var store = new FakeOidcSettingsStore();
        var context = CreateContext(new
        {
            enabled = true,
            displayName = (string?)null,
            authority = (string?)null,
            clientId = "client-1",
            clientSecret = "secret-1",
            scopes = "openid profile email",
            roleClaimName = "roles",
            defaultRole = "Viewer",
        });

        var result = await OidcSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Put_Returns400_WhenEnablingWithNoClientSecretEverSaved()
    {
        var store = new FakeOidcSettingsStore();
        var context = CreateContext(new
        {
            enabled = true,
            displayName = "Okta",
            authority = "https://example.okta.com",
            clientId = "client-1",
            clientSecret = (string?)null,
            scopes = "openid profile email",
            roleClaimName = "roles",
            defaultRole = "Viewer",
        });

        var result = await OidcSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Put_Returns400_WhenScopesIsBlank()
    {
        var store = new FakeOidcSettingsStore();
        var context = CreateContext(new
        {
            enabled = false,
            displayName = (string?)null,
            authority = (string?)null,
            clientId = (string?)null,
            clientSecret = (string?)null,
            scopes = "   ",
            roleClaimName = "roles",
            defaultRole = "Viewer",
        });

        var result = await OidcSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Put_AllowsDisabling_WithoutSupplyingCredentials()
    {
        var store = new FakeOidcSettingsStore(new OidcSettings(
            true, "Okta", "https://example.okta.com", "client-1", "secret-1", "openid profile email", "roles", UserRole.Viewer, DateTimeOffset.UtcNow));
        var context = CreateContext(new
        {
            enabled = false,
            displayName = "Okta",
            authority = "https://example.okta.com",
            clientId = "client-1",
            clientSecret = (string?)null,
            scopes = "openid profile email",
            roleClaimName = "roles",
            defaultRole = "Viewer",
        });

        var result = await OidcSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.False((await store.GetAsync()).Enabled);
    }
}
