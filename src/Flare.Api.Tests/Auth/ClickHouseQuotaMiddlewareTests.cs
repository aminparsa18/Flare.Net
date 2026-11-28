using System.Net;
using System.Security.Claims;
using ClickHouse.Driver;
using Flare.Api.Auth;
using Flare.Api.Demo;
using Flare.Api.Query;
using Flare.Identity.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flare.Api.Tests.Auth;

/// <summary>Per-identity ClickHouse quota keys and the 429 mapping (ADR-0286).</summary>
public class ClickHouseQuotaMiddlewareTests
{
    private static readonly IPAddress Address = IPAddress.Parse("203.0.113.7");

    [Fact]
    public void KeyFor_Unauthenticated_IsNull()
    {
        Assert.Null(ClickHouseQuotaMiddleware.KeyFor(new ClaimsPrincipal(new ClaimsIdentity()), Address));
    }

    [Fact]
    public void KeyFor_SignedInUser_IsTheUserId()
    {
        var user = Principal(new Claim(ClaimTypes.NameIdentifier, "8d1c"));

        Assert.Equal("user:8d1c", ClickHouseQuotaMiddleware.KeyFor(user, Address));
    }

    [Fact]
    public void KeyFor_PersonalAccessToken_IsTheTokenNotItsOwner()
    {
        var user = Principal(new Claim(ClaimTypes.NameIdentifier, "8d1c"), new Claim(FlareClaimTypes.PersonalAccessTokenId, "t9"));

        Assert.Equal("pat:t9", ClickHouseQuotaMiddleware.KeyFor(user, Address));
    }

    [Fact]
    public void KeyFor_DemoAccount_IsTheCallerAddress()
    {
        // Every demo visitor is the same account, so one visitor mustn't use up everyone's quota.
        var user = Principal(new Claim(ClaimTypes.NameIdentifier, "8d1c"), new Claim(DemoMode.ClaimType, "true"));

        Assert.Equal("demo:203.0.113.7", ClickHouseQuotaMiddleware.KeyFor(user, Address));
    }

    [Fact]
    public void IsQuotaExceeded_MatchesCode201_DirectOrWrapped()
    {
        var quota = new ClickHouseServerException("Quota for user `flare_query` for 3600s has been exceeded", "SELECT 1", 201);

        Assert.True(ClickHouseQuotaMiddleware.IsQuotaExceeded(quota));
        Assert.True(ClickHouseQuotaMiddleware.IsQuotaExceeded(new AggregateException(new InvalidOperationException(), quota)));
        Assert.True(ClickHouseQuotaMiddleware.IsQuotaExceeded(new InvalidOperationException("wrapped", quota)));
        Assert.False(ClickHouseQuotaMiddleware.IsQuotaExceeded(new ClickHouseServerException("Timeout exceeded", "SELECT 1", 159)));
        Assert.False(ClickHouseQuotaMiddleware.IsQuotaExceeded(new InvalidOperationException()));
    }

    [Fact]
    public async Task Invoke_SetsTheKeyForTheRequest_AndClearsItAfter()
    {
        string? seen = null;
        var middleware = new ClickHouseQuotaMiddleware(_ => { seen = ClickHouseQuotaKey.Current; return Task.CompletedTask; }, NullLogger<ClickHouseQuotaMiddleware>.Instance);
        var context = new DefaultHttpContext { User = Principal(new Claim(ClaimTypes.NameIdentifier, "8d1c")) };

        await middleware.InvokeAsync(context);

        Assert.Equal("user:8d1c", seen);
        Assert.Null(ClickHouseQuotaKey.Current);
    }

    [Fact]
    public async Task Invoke_QuotaExceeded_Returns429()
    {
        var middleware = new ClickHouseQuotaMiddleware(
            _ => throw new ClickHouseServerException("Quota exceeded", "SELECT 1", 201),
            NullLogger<ClickHouseQuotaMiddleware>.Instance);
        var context = new DefaultHttpContext();

        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status429TooManyRequests, context.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_OtherClickHouseErrors_PropagateUnchanged()
    {
        var middleware = new ClickHouseQuotaMiddleware(
            _ => throw new ClickHouseServerException("Timeout exceeded", "SELECT 1", 159),
            NullLogger<ClickHouseQuotaMiddleware>.Instance);

        await Assert.ThrowsAsync<ClickHouseServerException>(() => middleware.InvokeAsync(new DefaultHttpContext()));
    }

    private static ClaimsPrincipal Principal(params Claim[] claims) => new(new ClaimsIdentity(claims, "test"));
}
