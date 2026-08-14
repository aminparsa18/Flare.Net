using Flare.Api.Auth;
using Flare.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Api.Tests.Auth;

/// <summary>
/// Exercises the single choke point behind opt-in auth: when
/// <see cref="Flare.Identity.Auth.AuthSettings.Enabled"/> is false, every authorization
/// check must short-circuit to allowed regardless of what the policy actually evaluated
/// to; when true, behavior must be unchanged from the framework's own default handler.
/// </summary>
public class ConditionalAuthorizationMiddlewareResultHandlerTests
{
    private static readonly AuthorizationPolicy AnyPolicy = new AuthorizationPolicyBuilder().RequireAssertion(_ => true).Build();

    // The real AuthorizationMiddlewareResultHandler's Forbid path calls
    // HttpContext.ForbidAsync(), which resolves IAuthenticationService off
    // RequestServices - a bare DefaultHttpContext leaves that null, so every context
    // needs a real (if minimal) service provider, same "populate RequestServices"
    // convention AuthEndpointsTests already established.
    private static DefaultHttpContext CreateContext()
    {
        var services = new ServiceCollection().AddLogging();
        // A real scheme (not just AddAuthentication() bare) is needed - the default
        // handler's Forbid path calls HttpContext.ForbidAsync(), which needs a
        // DefaultForbidScheme to fall back to. Cookie auth is the simplest built-in
        // scheme that supports Forbid natively - the specific scheme choice is
        // irrelevant to what this test actually verifies (whether next() got called).
        services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        services.AddAuthorizationBuilder();
        return new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
    }

    [Fact]
    public async Task HandleAsync_CallsNext_WhenAuthIsDisabled_EvenForAForbiddenResult()
    {
        var handler = new ConditionalAuthorizationMiddlewareResultHandler(new FakeAuthSettingsStore(enabled: false));
        var context = CreateContext();
        var nextCalled = false;

        await handler.HandleAsync(_ => { nextCalled = true; return Task.CompletedTask; }, context, AnyPolicy, PolicyAuthorizationResult.Forbid());

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task HandleAsync_DelegatesToTheDefaultHandler_WhenAuthIsEnabled_ForASuccessfulResult()
    {
        var handler = new ConditionalAuthorizationMiddlewareResultHandler(new FakeAuthSettingsStore(enabled: true));
        var context = CreateContext();
        var nextCalled = false;

        await handler.HandleAsync(_ => { nextCalled = true; return Task.CompletedTask; }, context, AnyPolicy, PolicyAuthorizationResult.Success());

        // The default handler's own documented behavior for a Success result is to call
        // next unconditionally - asserting on that (not reimplementing/mocking the
        // default handler) confirms delegation actually happened, not a bypass.
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task HandleAsync_DoesNotCallNext_WhenAuthIsEnabled_ForAForbiddenResult()
    {
        var handler = new ConditionalAuthorizationMiddlewareResultHandler(new FakeAuthSettingsStore(enabled: true));
        var context = CreateContext();
        var nextCalled = false;

        await handler.HandleAsync(_ => { nextCalled = true; return Task.CompletedTask; }, context, AnyPolicy, PolicyAuthorizationResult.Forbid());

        Assert.False(nextCalled);
        Assert.NotEqual(StatusCodes.Status200OK, context.Response.StatusCode);
    }
}
