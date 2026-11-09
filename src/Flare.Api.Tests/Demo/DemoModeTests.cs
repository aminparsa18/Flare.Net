using System.Security.Claims;
using Flare.Api.Demo;
using Flare.Api.Tests.TestSupport;
using Flare.Identity.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Api.Tests.Demo;

/// <summary>Public read-only demo mode (ADR-0278): who a request runs as, what the demo user may
/// call, how the demo account is provisioned, and that auth can't be switched off.</summary>
public class DemoModeTests
{
    private static async Task<(DemoMode Demo, User DemoUser)> CreateDemoAsync(FakeUserStore? users = null)
    {
        var options = new DemoOptions { Enabled = true };
        var demo = new DemoMode(Options.Create(options));
        var user = await DemoProvisioner.ApplyAsync(users ?? new FakeUserStore(), options, NullLogger.Instance);
        demo.SetUser(user);
        return (demo, user);
    }

    private static DefaultHttpContext CreateContext(string method, bool allowInDemo, ClaimsPrincipal? user = null)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
        context.Request.Method = method;
        if (user is not null)
        {
            context.User = user;
        }

        var metadata = allowInDemo ? new EndpointMetadataCollection(new AllowInDemoAttribute()) : EndpointMetadataCollection.Empty;
        context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "test"));
        return context;
    }

    private static async Task<(bool NextCalled, HttpContext Context)> RunAsync(DemoMode demo, HttpContext context)
    {
        var nextCalled = false;
        await new DemoMiddleware(_ => { nextCalled = true; return Task.CompletedTask; }, demo).InvokeAsync(context);
        return (nextCalled, context);
    }

    private static ClaimsPrincipal SignedIn(UserRole role) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()), new Claim(ClaimTypes.Role, role.ToString())], "Session"));

    [Fact]
    public async Task AnonymousRequest_RunsAsTheDemoViewer()
    {
        var (demo, demoUser) = await CreateDemoAsync();

        var (nextCalled, context) = await RunAsync(demo, CreateContext("GET", allowInDemo: false));

        Assert.True(nextCalled);
        Assert.True(DemoMode.IsDemo(context.User));
        Assert.True(context.User.IsInRole(nameof(UserRole.Viewer)));
        Assert.Equal(demoUser.Id.ToString(), context.User.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    [Fact]
    public async Task SignedInRequest_KeepsItsOwnPrincipal()
    {
        var (demo, _) = await CreateDemoAsync();
        var admin = SignedIn(UserRole.Admin);

        var (nextCalled, context) = await RunAsync(demo, CreateContext("PUT", allowInDemo: false, admin));

        Assert.True(nextCalled);
        Assert.Same(admin, context.User);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task DemoWrite_ToAnUntaggedEndpoint_IsRefused(string method)
    {
        var (demo, _) = await CreateDemoAsync();

        var (nextCalled, context) = await RunAsync(demo, CreateContext(method, allowInDemo: false));

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, context.Response.StatusCode);
    }

    [Fact]
    public async Task DemoPost_ToAReadOnlyTaggedEndpoint_IsAllowed()
    {
        var (demo, _) = await CreateDemoAsync();

        var (nextCalled, _) = await RunAsync(demo, CreateContext("POST", allowInDemo: true));

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task RealViewer_IsNotSubjectToTheDemoWriteBlock()
    {
        var (demo, _) = await CreateDemoAsync();

        var (nextCalled, _) = await RunAsync(demo, CreateContext("POST", allowInDemo: false, SignedIn(UserRole.Viewer)));

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task Provisioner_CreatesAViewerNobodyCanSignInto()
    {
        var users = new FakeUserStore();

        var user = await DemoProvisioner.ApplyAsync(users, new DemoOptions(), NullLogger.Instance);

        Assert.Equal(UserRole.Viewer, user.Role);
        Assert.Equal("demo", user.Username);
        Assert.Null(await users.VerifyPasswordAsync("demo", "demo"));
    }

    [Fact]
    public async Task Provisioner_ResetsAnExistingAccountToAnEnabledViewer_AndRotatesItsPassword()
    {
        var users = new FakeUserStore();
        var existing = await users.CreateAsync("demo", "Known-password-1", UserRole.Member);
        await users.SetDisabledAsync(existing.Id, true);

        var user = await DemoProvisioner.ApplyAsync(users, new DemoOptions(), NullLogger.Instance);

        var stored = await users.FindByIdAsync(existing.Id);
        Assert.Equal(existing.Id, user.Id);
        Assert.Equal(UserRole.Viewer, stored!.Role);
        Assert.False(stored.IsDisabled);
        Assert.Null(await users.VerifyPasswordAsync("demo", "Known-password-1"));
    }

    [Fact]
    public async Task Provisioner_RefusesToTakeOverAnAdmin()
    {
        var users = new FakeUserStore();
        await users.CreateAsync("demo", "Known-password-1", UserRole.Admin);

        await Assert.ThrowsAsync<InvalidOperationException>(() => DemoProvisioner.ApplyAsync(users, new DemoOptions(), NullLogger.Instance));
        Assert.Equal(UserRole.Admin, (await users.FindByUsernameAsync("demo"))!.Role);
    }

    [Fact]
    public async Task SettingsStore_AlwaysReportsAuthAndLocalLoginOn_EvenAfterSavingThemOff()
    {
        var inner = new FakeAuthSettingsStore(enabled: false, localEnabled: false);
        var store = new DemoAuthSettingsStore(inner);

        var read = await store.GetAsync();
        await store.SaveAsync(enabled: false, localEnabled: false);

        Assert.True(read.Enabled);
        Assert.True(read.LocalEnabled);
        Assert.True((await inner.GetAsync()).Enabled);
        Assert.True((await inner.GetAsync()).LocalEnabled);
    }
}
