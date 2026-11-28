using System.Net;
using System.Security.Claims;
using ClickHouse.Driver;
using Flare.Api.Demo;
using Flare.Api.Query;
using Flare.Identity.Auth;

namespace Flare.Api.Auth;

/// <summary>
/// Charges the request's ClickHouse queries to the caller (ADR-0286): sets <see cref="ClickHouseQuotaKey.Current"/>
/// from the authenticated principal, and turns ClickHouse's <c>QUOTA_EXCEEDED</c> into a 429 instead of a 500.
/// Runs after authentication. Unauthenticated requests (auth disabled) get no key, so ClickHouse charges them to
/// the <c>flare_query</c> user-name bucket.
/// </summary>
public sealed class ClickHouseQuotaMiddleware(RequestDelegate next, ILogger<ClickHouseQuotaMiddleware> logger)
{
    /// <summary>ClickHouse error code 201.</summary>
    public const int QuotaExceeded = 201;

    public async Task InvokeAsync(HttpContext context)
    {
        var key = KeyFor(context.User, context.Connection.RemoteIpAddress);
        ClickHouseQuotaKey.Current = key;
        try
        {
            await next(context);
        }
        catch (Exception ex) when (IsQuotaExceeded(ex) && !context.Response.HasStarted)
        {
            logger.LogWarning("ClickHouse query quota exceeded for {QuotaKey} on {Path}.", key ?? "(no key)", context.Request.Path);
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            await context.Response.WriteAsync("Query quota exceeded. Try again later.", context.RequestAborted);
        }
        finally
        {
            ClickHouseQuotaKey.Current = null;
        }
    }

    /// <summary>
    /// <c>pat:{id}</c> for a personal access token, <c>demo:{address}</c> for the shared demo account (ADR-0278),
    /// <c>user:{id}</c> for any other signed-in account, null when unauthenticated. A PAT gets its own bucket
    /// rather than its owner's, matching the per-token request limit (ADR-0028).
    /// </summary>
    public static string? KeyFor(ClaimsPrincipal user, IPAddress? remoteAddress)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        if (user.FindFirstValue(FlareClaimTypes.PersonalAccessTokenId) is { Length: > 0 } patId)
        {
            return "pat:" + patId;
        }

        if (DemoMode.IsDemo(user))
        {
            return "demo:" + (remoteAddress?.ToString() ?? "unknown");
        }

        return user.FindFirstValue(ClaimTypes.NameIdentifier) is { Length: > 0 } userId ? "user:" + userId : null;
    }

    /// <summary>Whether <paramref name="ex"/> is, or wraps, ClickHouse's quota error.</summary>
    public static bool IsQuotaExceeded(Exception ex) => ex switch
    {
        ClickHouseServerException { ErrorCode: QuotaExceeded } => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(IsQuotaExceeded),
        { InnerException: { } inner } => IsQuotaExceeded(inner),
        _ => false,
    };
}
