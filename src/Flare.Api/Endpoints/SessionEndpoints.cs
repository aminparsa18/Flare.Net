using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.Auth;
using Microsoft.Extensions.Options;

namespace Flare.Api.Endpoints;

/// <summary>
/// Self-service management of the caller's own login sessions, under <c>/api/auth/sessions</c>
/// (any authenticated user; mapped onto the plain <c>RequireAuthorization()</c> group).
/// Sessions are listed by a one-way handle (<see cref="HandleFor"/>), never the token, since the
/// token is the cookie value. A caller authenticated by PAT has no current session, so
/// <c>IsCurrent</c> is false everywhere and "keep current" keeps nothing.
/// </summary>
public static class SessionEndpoints
{
    public static IEndpointRouteBuilder MapSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/sessions", HandleListAsync);
        endpoints.MapDelete("/api/auth/sessions/{handle}", HandleRevokeAsync);
        endpoints.MapDelete("/api/auth/sessions", HandleRevokeAllAsync);
        return endpoints;
    }

    /// <summary>First 16 bytes of SHA-256(token), hex - stable per session, not reversible.</summary>
    internal static string HandleFor(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)).AsSpan(0, 16)).ToLowerInvariant();

    internal static async Task<IResult> HandleListAsync(
        HttpContext http, ClaimsPrincipal principal, ISessionStore sessions, IOptions<AuthOptions> authOptions, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        var currentHandle = CurrentToken(http, authOptions) is { } token ? HandleFor(token) : null;
        var list = await sessions.ListForUserAsync(userId, cancellationToken);
        var dtos = list.Select(s => new SessionDto
        {
            Id = HandleFor(s.Id),
            CreatedAt = s.CreatedAt,
            LastSeenAt = s.LastSeenAt,
            ExpiresAt = s.ExpiresAt,
            IsCurrent = HandleFor(s.Id) == currentHandle
        }).ToList();
        return ApiSerialization.Write(http, new SessionListResponse { Sessions = dtos }, SessionsJsonContext.Default.SessionListResponse);
    }

    internal static async Task<IResult> HandleRevokeAsync(
        string handle, ClaimsPrincipal principal, ISessionStore sessions, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        // Only ever resolves among the caller's own sessions, so one user can't revoke another's.
        var match = (await sessions.ListForUserAsync(userId, cancellationToken)).FirstOrDefault(s => HandleFor(s.Id) == handle);
        if (match is null)
        {
            return Results.NotFound();
        }

        await sessions.DeleteAsync(match.Id, cancellationToken);
        return Results.NoContent();
    }

    /// <summary>Signs out everywhere; <c>?keepCurrent=true</c> spares the calling session.</summary>
    internal static async Task<IResult> HandleRevokeAllAsync(
        bool? keepCurrent, HttpContext http, ClaimsPrincipal principal, ISessionStore sessions, IOptions<AuthOptions> authOptions, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        if (keepCurrent == true && CurrentToken(http, authOptions) is { } token)
        {
            await sessions.DeleteAllForUserExceptAsync(userId, token, cancellationToken);
        }
        else
        {
            await sessions.DeleteAllForUserAsync(userId, cancellationToken);
        }

        return Results.NoContent();
    }

    private static string? CurrentToken(HttpContext http, IOptions<AuthOptions> authOptions) =>
        http.Request.Cookies.TryGetValue(authOptions.Value.CookieName, out var token) && !string.IsNullOrEmpty(token) ? token : null;

    private static bool TryGetUserId(ClaimsPrincipal principal, out Guid userId)
    {
        userId = Guid.Empty;
        return principal.Identity is { IsAuthenticated: true }
            && Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);
    }
}
