using System.Security.Claims;
using System.Text.Json;
using Flare.Api.Endpoints;
using Flare.Api.Json;
using Flare.Api.Tests.TestSupport;
using Flare.Identity.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Api.Tests.Endpoints;

/// <summary>Self-service session management: handle-based listing (never the token), per-user
/// scoping of revoke, and sign-out-everywhere with/without keeping the calling session.</summary>
public class SessionEndpointsTests
{
    private static readonly IOptions<AuthOptions> Opts = Options.Create(new AuthOptions());
    private static readonly IServiceProvider Services = new ServiceCollection().AddLogging().BuildServiceProvider();

    private static ClaimsPrincipal Principal(Guid userId) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"));

    private static DefaultHttpContext Ctx(string? cookieToken = null)
    {
        var c = new DefaultHttpContext { RequestServices = Services };
        c.Response.Body = new MemoryStream();
        if (cookieToken is not null)
        {
            c.Request.Headers.Cookie = $"{Opts.Value.CookieName}={cookieToken}";
        }
        return c;
    }

    private static async Task<int> Run(IResult r, DefaultHttpContext c)
    {
        await r.ExecuteAsync(c);
        return c.Response.StatusCode;
    }

    [Fact]
    public async Task List_ReturnsHandlesNotTokens_AndFlagsCurrent()
    {
        var sessions = new FakeSessionStore();
        var userId = Guid.NewGuid();
        var mine = await sessions.CreateAsync(userId, TimeSpan.FromHours(1));
        var other = await sessions.CreateAsync(userId, TimeSpan.FromHours(1));
        await sessions.CreateAsync(Guid.NewGuid(), TimeSpan.FromHours(1));

        var c = Ctx(mine.Id);
        Assert.Equal(200, await Run(await SessionEndpoints.HandleListAsync(c, Principal(userId), sessions, Opts, default), c));
        c.Response.Body.Position = 0;
        var body = await new StreamReader(c.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain(mine.Id, body);
        Assert.DoesNotContain(other.Id, body);

        var list = JsonSerializer.Deserialize(body, SessionsJsonContext.Default.SessionListResponse)!;
        Assert.Equal(2, list.Sessions.Count);
        Assert.Equal(SessionEndpoints.HandleFor(mine.Id), Assert.Single(list.Sessions, s => s.IsCurrent).Id);
    }

    [Fact]
    public async Task Revoke_OnlyAffectsCallersOwnSessions()
    {
        var sessions = new FakeSessionStore();
        var userId = Guid.NewGuid();
        var mine = await sessions.CreateAsync(userId, TimeSpan.FromHours(1));
        var theirs = await sessions.CreateAsync(Guid.NewGuid(), TimeSpan.FromHours(1));

        var c1 = Ctx();
        Assert.Equal(404, await Run(await SessionEndpoints.HandleRevokeAsync(SessionEndpoints.HandleFor(theirs.Id), Principal(userId), sessions, default), c1));
        Assert.True(sessions.Contains(theirs.Id));

        var c2 = Ctx();
        Assert.Equal(204, await Run(await SessionEndpoints.HandleRevokeAsync(SessionEndpoints.HandleFor(mine.Id), Principal(userId), sessions, default), c2));
        Assert.False(sessions.Contains(mine.Id));
    }

    [Fact]
    public async Task RevokeAll_KeepsCurrentOnlyWhenAsked()
    {
        var sessions = new FakeSessionStore();
        var userId = Guid.NewGuid();
        var mine = await sessions.CreateAsync(userId, TimeSpan.FromHours(1));
        var other = await sessions.CreateAsync(userId, TimeSpan.FromHours(1));

        var keep = Ctx(mine.Id);
        Assert.Equal(204, await Run(await SessionEndpoints.HandleRevokeAllAsync(true, keep, Principal(userId), sessions, Opts, default), keep));
        Assert.True(sessions.Contains(mine.Id));
        Assert.False(sessions.Contains(other.Id));

        var all = Ctx(mine.Id);
        Assert.Equal(204, await Run(await SessionEndpoints.HandleRevokeAllAsync(null, all, Principal(userId), sessions, Opts, default), all));
        Assert.False(sessions.Contains(mine.Id));
    }

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        var c = Ctx();
        Assert.Equal(401, await Run(await SessionEndpoints.HandleListAsync(c, new ClaimsPrincipal(new ClaimsIdentity()), new FakeSessionStore(), Opts, default), c));
    }
}
