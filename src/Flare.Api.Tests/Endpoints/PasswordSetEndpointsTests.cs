using System.Security.Claims;
using System.Text.Json;
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

/// <summary>Invite, admin reset, set-password redemption and self-service change.</summary>
public class PasswordSetEndpointsTests
{
    private static readonly IServiceProvider Services = new ServiceCollection().AddLogging().BuildServiceProvider();

    private static DefaultHttpContext Ctx(object? body = null, string? cookie = null)
    {
        var c = new DefaultHttpContext { RequestServices = Services };
        if (body is not null)
        {
            c.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body)));
        }
        if (cookie is not null)
        {
            c.Request.Headers.Cookie = $"{new AuthOptions().CookieName}={cookie}";
        }
        c.Response.Body = new MemoryStream();
        return c;
    }

    private static async Task<int> Run(IResult r, DefaultHttpContext c)
    {
        await r.ExecuteAsync(c);
        return c.Response.StatusCode;
    }

    private static async Task<PasswordSetLinkResponse> ReadLink(DefaultHttpContext c)
    {
        c.Response.Body.Position = 0;
        return (await JsonSerializer.DeserializeAsync(c.Response.Body, UsersJsonContext.Default.PasswordSetLinkResponse))!;
    }

    [Fact]
    public async Task Invite_ThenSetPassword_AllowsLogin_AndTokenIsSingleUse()
    {
        var users = new FakeUserStore();
        var tokens = new FakePasswordSetTokenStore();
        var sessions = new FakeSessionStore();

        var ic = Ctx(new { username = "newbie", role = "Member" });
        Assert.Equal(201, await Run(await UserEndpoints.HandleInviteAsync(ic, users, tokens, new FakeAuthSettingsStore(), new FakePasswordResetMailer(), default), ic));
        var link = await ReadLink(ic);

        var sc = Ctx(new { token = link.Token, password = "brand-new-pass" });
        Assert.Equal(204, await Run(await AuthEndpoints.HandleSetPasswordAsync(sc, users, sessions, tokens, default), sc));
        Assert.NotNull(await users.VerifyPasswordAsync("newbie", "brand-new-pass"));

        var again = Ctx(new { token = link.Token, password = "another-pass-1" });
        Assert.Equal(400, await Run(await AuthEndpoints.HandleSetPasswordAsync(again, users, sessions, tokens, default), again));
    }

    [Fact]
    public async Task Invite_EmailUsername_SendsEmail_PlainUsernameDoesNot()
    {
        var mailer = new FakePasswordResetMailer();
        var users = new FakeUserStore();
        var tokens = new FakePasswordSetTokenStore();
        var a = Ctx(new { username = "new@example.com", role = "Viewer" });
        Assert.Equal(201, await Run(await UserEndpoints.HandleInviteAsync(a, users, tokens, new FakeAuthSettingsStore(), mailer, default), a));
        Assert.True((await ReadLink(a)).EmailSent);
        var b = Ctx(new { username = "plain", role = "Viewer" });
        Assert.Equal(201, await Run(await UserEndpoints.HandleInviteAsync(b, users, tokens, new FakeAuthSettingsStore(), mailer, default), b));
        Assert.False((await ReadLink(b)).EmailSent);
        Assert.Single(mailer.Sent);
    }

    [Fact]
    public async Task Invite_DuplicateUsername_Conflicts()
    {
        var users = new FakeUserStore();
        await users.CreateAsync("taken", "password-123", UserRole.Viewer);
        var c = Ctx(new { username = "TAKEN", role = "Viewer" });
        Assert.Equal(409, await Run(await UserEndpoints.HandleInviteAsync(c, users, new FakePasswordSetTokenStore(), new FakeAuthSettingsStore(), new FakePasswordResetMailer(), default), c));
    }

    [Fact]
    public async Task SetPassword_TooShort_DoesNotConsumeToken()
    {
        var users = new FakeUserStore();
        var tokens = new FakePasswordSetTokenStore();
        var u = await users.CreateAsync("u", "password-123", UserRole.Viewer);
        var issued = await tokens.CreateAsync(u.Id, Flare.Identity.PasswordSetTokens.PasswordSetPurpose.Reset, TimeSpan.FromHours(1));

        var c = Ctx(new { token = issued.RawToken, password = "short" });
        Assert.Equal(400, await Run(await AuthEndpoints.HandleSetPasswordAsync(c, users, new FakeSessionStore(), tokens, default), c));
        Assert.NotNull(await tokens.ConsumeAsync(issued.RawToken));
    }

    [Fact]
    public async Task AdminReset_RevokesSessions_AndRejectsNonLocalAccounts()
    {
        var users = new FakeUserStore();
        var sessions = new FakeSessionStore();
        var tokens = new FakePasswordSetTokenStore();
        var local = await users.CreateAsync("local", "password-123", UserRole.Viewer);
        var sso = await users.CreateFromExternalAsync("Entra", "ext", "sso", UserRole.Viewer);
        var s = await sessions.CreateAsync(local.Id, TimeSpan.FromHours(1));

        var c = Ctx();
        Assert.Equal(200, await Run(await UserEndpoints.HandlePasswordResetAsync(local.Id, c, users, sessions, tokens, default), c));
        Assert.False(sessions.Contains(s.Id));

        var c2 = Ctx();
        Assert.Equal(400, await Run(await UserEndpoints.HandlePasswordResetAsync(sso.Id, c2, users, sessions, tokens, default), c2));
    }

    [Fact]
    public async Task ChangePassword_KeepsCurrentSession_RevokesOthers_AndChecksCurrentPassword()
    {
        var users = new FakeUserStore();
        var sessions = new FakeSessionStore();
        var u = await users.CreateAsync("me", "old-password-1", UserRole.Member);
        var mine = await sessions.CreateAsync(u.Id, TimeSpan.FromHours(1));
        var other = await sessions.CreateAsync(u.Id, TimeSpan.FromHours(1));
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, u.Id.ToString())], "test"));
        var opts = Options.Create(new AuthOptions());

        var bad = Ctx(new { currentPassword = "wrong", newPassword = "new-password-1" }, mine.Id);
        Assert.Equal(400, await Run(await AuthEndpoints.HandleChangePasswordAsync(bad, principal, users, sessions, opts, default), bad));
        Assert.True(sessions.Contains(other.Id));

        var ok = Ctx(new { currentPassword = "old-password-1", newPassword = "new-password-1" }, mine.Id);
        Assert.Equal(204, await Run(await AuthEndpoints.HandleChangePasswordAsync(ok, principal, users, sessions, opts, default), ok));
        Assert.True(sessions.Contains(mine.Id));
        Assert.False(sessions.Contains(other.Id));
        Assert.NotNull(await users.VerifyPasswordAsync("me", "new-password-1"));
    }
}
