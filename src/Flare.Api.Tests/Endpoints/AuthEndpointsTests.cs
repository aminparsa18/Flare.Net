using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Flare.Api.Endpoints;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Tests.TestSupport;
using Flare.Identity.Auth;
using Flare.Identity.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Api.Tests.Endpoints;

/// <summary>
/// Exercises <see cref="AuthEndpoints"/>' handlers directly (made <c>internal</c> for
/// this purpose - see <c>Flare.Api.csproj</c>'s <c>InternalsVisibleTo</c>) against fake,
/// in-memory <see cref="FakeUserStore"/>/<see cref="FakeSessionStore"/> - no SQLite/
/// ClickHouse/Redis/WebApplicationFactory involved, same "unit-test the logic, fake the
/// interfaces" convention this project already uses for its ClickHouse-free query
/// builder tests. Results are actually executed via <see cref="IResult.ExecuteAsync"/>
/// against a real <see cref="DefaultHttpContext"/> so status codes, response bodies, and
/// Set-Cookie headers are verified as they'd really appear on the wire, not just
/// inspected as in-memory objects.
/// </summary>
public class AuthEndpointsTests
{
    private static readonly AuthOptions DefaultAuthOptions = new();

    // Local login enabled by default, matching AuthSettings' own migration-time default -
    // most tests below aren't exercising the LocalEnabled gate itself, so they shouldn't
    // have to think about it.
    private static FakeAuthSettingsStore DefaultAuthSettings => new();

    // IResult.ExecuteAsync (Results.Json/Unauthorized/etc.) resolves services like
    // ILoggerFactory off HttpContext.RequestServices - a bare DefaultHttpContext leaves
    // that null, so every context needs at least an empty populated provider.
    private static readonly IServiceProvider EmptyRequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();

    private static DefaultHttpContext CreateContext(object? jsonBody = null)
    {
        var context = new DefaultHttpContext { RequestServices = EmptyRequestServices };
        if (jsonBody is not null)
        {
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(jsonBody)));
        }
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<T?> ReadJsonBodyAsync<T>(DefaultHttpContext context, JsonTypeInfo<T> typeInfo)
    {
        context.Response.Body.Position = 0;
        return await JsonSerializer.DeserializeAsync(context.Response.Body, typeInfo);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_ForAnUnknownUsername()
    {
        var context = CreateContext(new { username = "nobody", password = "whatever1" });

        var result = await AuthEndpoints.HandleLoginAsync(context, new FakeUserStore(), new FakeSessionStore(), DefaultAuthSettings, Options.Create(DefaultAuthOptions), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsUnauthorized_ForTheWrongPassword()
    {
        var users = new FakeUserStore();
        await users.CreateAsync("alice", "correctpassword1", UserRole.Admin);
        var context = CreateContext(new { username = "alice", password = "wrongpassword1" });

        var result = await AuthEndpoints.HandleLoginAsync(context, users, new FakeSessionStore(), DefaultAuthSettings, Options.Create(DefaultAuthOptions), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task Login_ReturnsTheUser_AndSetsASessionCookie_ForCorrectCredentials()
    {
        var users = new FakeUserStore();
        var created = await users.CreateAsync("alice", "correctpassword1", UserRole.Admin);
        var sessions = new FakeSessionStore();
        var context = CreateContext(new { username = "alice", password = "correctpassword1" });

        var result = await AuthEndpoints.HandleLoginAsync(context, users, sessions, DefaultAuthSettings, Options.Create(DefaultAuthOptions), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        var dto = await ReadJsonBodyAsync(context, AuthJsonContext.Default.AuthUserDto);
        Assert.Equal(created.Id, dto!.Id);
        Assert.Equal(UserRole.Admin, dto.Role);

        var setCookie = context.Response.Headers.SetCookie.ToString();
        Assert.Contains(DefaultAuthOptions.CookieName, setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);

        // The cookie's value is the session token itself (Session.Id doubles as both) -
        // confirm a real session was actually created, not just a cookie shape.
        var token = ExtractCookieValue(setCookie, DefaultAuthOptions.CookieName);
        Assert.NotNull(await sessions.FindAsync(token));
    }

    [Fact]
    public async Task Logout_DeletesTheSession_AndClearsTheCookie()
    {
        var users = new FakeUserStore();
        var user = await users.CreateAsync("alice", "correctpassword1", UserRole.Viewer);
        var sessions = new FakeSessionStore();
        var session = await sessions.CreateAsync(user.Id, TimeSpan.FromDays(1));

        var context = new DefaultHttpContext { RequestServices = EmptyRequestServices };
        context.Request.Headers.Cookie = $"{DefaultAuthOptions.CookieName}={session.Id}";
        context.Response.Body = new MemoryStream();

        var result = await AuthEndpoints.HandleLogoutAsync(context, users, sessions, new FakeProxyAuthSettingsStore(), Options.Create(DefaultAuthOptions), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Null(await sessions.FindAsync(session.Id));
        var dto = await ReadJsonBodyAsync(context, AuthJsonContext.Default.LogoutResponse);
        Assert.Null(dto!.RedirectUrl);
    }

    [Fact]
    public async Task Logout_ReturnsTheConfiguredRedirectUrl_ForAReverseProxyProvisionedAccount()
    {
        var users = new FakeUserStore();
        var user = await users.CreateFromExternalAsync("ReverseProxy", "alice", "alice", UserRole.Viewer);
        var sessions = new FakeSessionStore();
        var session = await sessions.CreateAsync(user.Id, TimeSpan.FromDays(1));
        var proxyAuthSettings = new FakeProxyAuthSettingsStore(new ProxyAuthSettings(
            true, "Remote-User", "172.18.0.0/16", null, null, null, null, UserRole.Viewer, DateTimeOffset.UtcNow,
            LogoutRedirectUrl: "https://proxy.example.com/oauth2/sign_out"));

        var context = new DefaultHttpContext { RequestServices = EmptyRequestServices };
        context.Request.Headers.Cookie = $"{DefaultAuthOptions.CookieName}={session.Id}";
        context.Response.Body = new MemoryStream();

        var result = await AuthEndpoints.HandleLogoutAsync(context, users, sessions, proxyAuthSettings, Options.Create(DefaultAuthOptions), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Null(await sessions.FindAsync(session.Id));
        var dto = await ReadJsonBodyAsync(context, AuthJsonContext.Default.LogoutResponse);
        Assert.Equal("https://proxy.example.com/oauth2/sign_out", dto!.RedirectUrl);
    }

    [Fact]
    public async Task Logout_ReturnsNoRedirectUrl_ForALocalAccount_EvenIfOneIsConfigured()
    {
        var users = new FakeUserStore();
        var user = await users.CreateAsync("alice", "correctpassword1", UserRole.Viewer);
        var sessions = new FakeSessionStore();
        var session = await sessions.CreateAsync(user.Id, TimeSpan.FromDays(1));
        var proxyAuthSettings = new FakeProxyAuthSettingsStore(new ProxyAuthSettings(
            true, "Remote-User", "172.18.0.0/16", null, null, null, null, UserRole.Viewer, DateTimeOffset.UtcNow,
            LogoutRedirectUrl: "https://proxy.example.com/oauth2/sign_out"));

        var context = new DefaultHttpContext { RequestServices = EmptyRequestServices };
        context.Request.Headers.Cookie = $"{DefaultAuthOptions.CookieName}={session.Id}";
        context.Response.Body = new MemoryStream();

        var result = await AuthEndpoints.HandleLogoutAsync(context, users, sessions, proxyAuthSettings, Options.Create(DefaultAuthOptions), CancellationToken.None);
        await result.ExecuteAsync(context);

        var dto = await ReadJsonBodyAsync(context, AuthJsonContext.Default.LogoutResponse);
        Assert.Null(dto!.RedirectUrl);
    }

    [Fact]
    public async Task Me_ReturnsUnauthorized_WhenThePrincipalIsNotAuthenticated()
    {
        var context = CreateContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity()); // unauthenticated - no IsAuthenticated scheme name

        var result = await AuthEndpoints.HandleMeAsync(context.User, new FakeUserStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public async Task Me_ReturnsTheUser_WhenThePrincipalCarriesAValidSessionClaim()
    {
        var users = new FakeUserStore();
        var user = await users.CreateAsync("alice", "correctpassword1", UserRole.Member);
        var context = CreateContext();
        // Mirrors exactly what SessionAuthenticationHandler builds on a successful cookie lookup.
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())], authenticationType: "FlareSession");
        context.User = new ClaimsPrincipal(identity);

        var result = await AuthEndpoints.HandleMeAsync(context.User, users, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        var dto = await ReadJsonBodyAsync(context, AuthJsonContext.Default.AuthUserDto);
        Assert.Equal(user.Id, dto!.Id);
    }

    [Fact]
    public async Task Bootstrap_CreatesAnAdminAndSignsIn_WhenNoUsersExistYet()
    {
        var users = new FakeUserStore();
        var sessions = new FakeSessionStore();
        var context = CreateContext(new { username = "root", password = "correctpassword1" });

        var result = await AuthEndpoints.HandleBootstrapAsync(context, users, sessions, DefaultAuthSettings, Options.Create(DefaultAuthOptions), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
        var dto = await ReadJsonBodyAsync(context, AuthJsonContext.Default.AuthUserDto);
        Assert.Equal(UserRole.Admin, dto!.Role);
        Assert.Contains(DefaultAuthOptions.CookieName, context.Response.Headers.SetCookie.ToString());
    }

    [Fact]
    public async Task Bootstrap_Returns409_WhenAUserAlreadyExists()
    {
        var users = new FakeUserStore();
        await users.CreateAsync("existing-admin", "correctpassword1", UserRole.Admin);
        var context = CreateContext(new { username = "root", password = "correctpassword1" });

        var result = await AuthEndpoints.HandleBootstrapAsync(context, users, new FakeSessionStore(), DefaultAuthSettings, Options.Create(DefaultAuthOptions), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status409Conflict, context.Response.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_Returns400_ForAPasswordShorterThanEightCharacters()
    {
        var context = CreateContext(new { username = "root", password = "short1" });

        var result = await AuthEndpoints.HandleBootstrapAsync(context, new FakeUserStore(), new FakeSessionStore(), DefaultAuthSettings, Options.Create(DefaultAuthOptions), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task BootstrapStatus_ReturnsTrue_WhenNoUsersExist()
    {
        var context = CreateContext();

        var result = await AuthEndpoints.HandleBootstrapStatusAsync(new FakeUserStore(), new FakeEntraSettingsStore(), DefaultAuthSettings, new FakeLdapSettingsStore(), new FakeOidcSettingsStore(), new FakeProxyAuthSettingsStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        var dto = await ReadJsonBodyAsync(context, AuthJsonContext.Default.BootstrapStatusResponse);
        Assert.True(dto!.NeedsBootstrap);
    }

    [Fact]
    public async Task BootstrapStatus_ReturnsFalse_OnceAUserExists()
    {
        var users = new FakeUserStore();
        await users.CreateAsync("alice", "correctpassword1", UserRole.Admin);
        var context = CreateContext();

        var result = await AuthEndpoints.HandleBootstrapStatusAsync(users, new FakeEntraSettingsStore(), DefaultAuthSettings, new FakeLdapSettingsStore(), new FakeOidcSettingsStore(), new FakeProxyAuthSettingsStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        var dto = await ReadJsonBodyAsync(context, AuthJsonContext.Default.BootstrapStatusResponse);
        Assert.False(dto!.NeedsBootstrap);
    }

    [Fact]
    public async Task BootstrapStatus_ReportsEntraEnabled_WhenConfigured()
    {
        var context = CreateContext();
        var entraSettings = new FakeEntraSettingsStore(new EntraSettings(Enabled: true, TenantId: "tenant-1", ClientId: "client-1", ClientSecret: "secret-1", UpdatedAt: DateTimeOffset.UtcNow));

        var result = await AuthEndpoints.HandleBootstrapStatusAsync(new FakeUserStore(), entraSettings, DefaultAuthSettings, new FakeLdapSettingsStore(), new FakeOidcSettingsStore(), new FakeProxyAuthSettingsStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        var dto = await ReadJsonBodyAsync(context, AuthJsonContext.Default.BootstrapStatusResponse);
        Assert.True(dto!.EntraEnabled);
    }

    [Fact]
    public async Task BootstrapStatus_ReportsLdapEnabled_WhenConfigured()
    {
        var context = CreateContext();
        var ldapSettings = new FakeLdapSettingsStore(new LdapSettings(
            true, "dc.corp.example.com", 636, true, "DC=corp,DC=example,DC=com", "CN=svc,DC=corp,DC=example,DC=com",
            "secret", "(&(objectClass=user)(sAMAccountName={0}))", "objectGUID", null, null, null,
            UserRole.Viewer, DateTimeOffset.UtcNow));

        var result = await AuthEndpoints.HandleBootstrapStatusAsync(new FakeUserStore(), new FakeEntraSettingsStore(), DefaultAuthSettings, ldapSettings, new FakeOidcSettingsStore(), new FakeProxyAuthSettingsStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        var dto = await ReadJsonBodyAsync(context, AuthJsonContext.Default.BootstrapStatusResponse);
        Assert.True(dto!.LdapEnabled);
    }

    [Fact]
    public async Task BootstrapStatus_ReportsOidcEnabled_WhenConfigured()
    {
        var context = CreateContext();
        var oidcSettings = new FakeOidcSettingsStore(new OidcSettings(
            true, "Okta", "https://example.okta.com", "client-1", "secret-1", "openid profile email", "roles", UserRole.Viewer, DateTimeOffset.UtcNow));

        var result = await AuthEndpoints.HandleBootstrapStatusAsync(new FakeUserStore(), new FakeEntraSettingsStore(), DefaultAuthSettings, new FakeLdapSettingsStore(), oidcSettings, new FakeProxyAuthSettingsStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        var dto = await ReadJsonBodyAsync(context, AuthJsonContext.Default.BootstrapStatusResponse);
        Assert.True(dto!.OidcEnabled);
    }

    [Fact]
    public async Task BootstrapStatus_ReportsProxyAuthEnabled_WhenConfigured()
    {
        var context = CreateContext();
        var proxyAuthSettings = new FakeProxyAuthSettingsStore(new ProxyAuthSettings(
            true, "Remote-User", "172.18.0.0/16", null, null, null, null, UserRole.Viewer, DateTimeOffset.UtcNow));

        var result = await AuthEndpoints.HandleBootstrapStatusAsync(new FakeUserStore(), new FakeEntraSettingsStore(), DefaultAuthSettings, new FakeLdapSettingsStore(), new FakeOidcSettingsStore(), proxyAuthSettings, CancellationToken.None);
        await result.ExecuteAsync(context);

        var dto = await ReadJsonBodyAsync(context, AuthJsonContext.Default.BootstrapStatusResponse);
        Assert.True(dto!.ProxyAuthEnabled);
    }

    [Fact]
    public async Task BootstrapStatus_ReportsAuthEnabledAndLocalEnabled_FromAuthSettings()
    {
        var context = CreateContext();
        var authSettings = new FakeAuthSettingsStore(enabled: false, localEnabled: true);

        var result = await AuthEndpoints.HandleBootstrapStatusAsync(new FakeUserStore(), new FakeEntraSettingsStore(), authSettings, new FakeLdapSettingsStore(), new FakeOidcSettingsStore(), new FakeProxyAuthSettingsStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        var dto = await ReadJsonBodyAsync(context, AuthJsonContext.Default.BootstrapStatusResponse);
        Assert.False(dto!.AuthEnabled);
        Assert.True(dto.LocalEnabled);
    }

    [Fact]
    public async Task Login_Returns404_WhenLocalLoginIsDisabled()
    {
        var users = new FakeUserStore();
        await users.CreateAsync("alice", "correctpassword1", UserRole.Admin);
        var context = CreateContext(new { username = "alice", password = "correctpassword1" });

        var result = await AuthEndpoints.HandleLoginAsync(context, users, new FakeSessionStore(), new FakeAuthSettingsStore(localEnabled: false), Options.Create(DefaultAuthOptions), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task Bootstrap_Returns404_WhenLocalLoginIsDisabled()
    {
        var context = CreateContext(new { username = "root", password = "correctpassword1" });

        var result = await AuthEndpoints.HandleBootstrapAsync(context, new FakeUserStore(), new FakeSessionStore(), new FakeAuthSettingsStore(localEnabled: false), Options.Create(DefaultAuthOptions), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    private static string ExtractCookieValue(string setCookieHeader, string cookieName)
    {
        var prefix = $"{cookieName}=";
        var start = setCookieHeader.IndexOf(prefix, StringComparison.Ordinal) + prefix.Length;
        var end = setCookieHeader.IndexOf(';', start);
        return setCookieHeader[start..(end < 0 ? setCookieHeader.Length : end)];
    }
}
