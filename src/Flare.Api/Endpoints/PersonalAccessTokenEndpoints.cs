using System.Security.Claims;
using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.PersonalAccessTokens;
using Flare.Identity.Users;

namespace Flare.Api.Endpoints;

/// <summary>
/// Manages personal access tokens, under <c>/api/access-tokens</c> - self-service (mapped
/// onto <c>Program.cs</c>'s plain <c>RequireAuthorization()</c> group, i.e. any
/// authenticated Viewer-and-up, not <c>RequireAdmin</c>/<c>RequireMember</c>). A PAT
/// authenticates as the user who created it with exactly that user's own role/permissions
/// (see <c>SessionAuthenticationHandler</c>), so letting a user mint one for themselves is
/// no more privileged than that user already logging in - unlike ingest API keys
/// (<see cref="IngestApiKeyEndpoints"/>, admin-only, because a leaked one lets anyone
/// ingest telemetry as this whole Flare instance).
/// <para/>
/// Deliberately no admin oversight endpoint (list/revoke *other* users' tokens) yet -
/// out of scope for the roadmap item this closes (docs-internal/planning/roadmap.md's
/// former "No API tokens for the query API" entry just asked for user-scoped bearer
/// tokens); an Admin can still fully neutralize another user's tokens today by disabling
/// that account (every session AND every PAT check re-resolves the owning
/// <see cref="User"/> and rejects <see cref="User.IsDisabled"/> - see
/// <c>SessionAuthenticationHandler</c>). Revisit if that turns out not to be enough in
/// practice.
/// </summary>
public static class PersonalAccessTokenEndpoints
{
    /// <summary>Matches GitHub's fine-grained PAT range (1 day - 1 year) loosely; the
    /// upper bound exists so "no expiration" (<c>null</c>) is a deliberate opt-in rather
    /// than something a caller backs into with a huge number.</summary>
    private const int MaxExpiresInDays = 365;

    public static IEndpointRouteBuilder MapPersonalAccessTokenEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/access-tokens", HandleCreateAsync);
        endpoints.MapGet("/api/access-tokens", HandleListAsync);
        endpoints.MapDelete("/api/access-tokens/{id:guid}", HandleRevokeAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(
        HttpContext http, ClaimsPrincipal principal, IPersonalAccessTokenStore tokens, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        CreateAccessTokenRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, PersonalAccessTokensJsonContext.Default.CreateAccessTokenRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Name))
        {
            return Results.Problem("Name is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ExpiresInDays is < 1 or > MaxExpiresInDays)
        {
            return Results.Problem($"ExpiresInDays must be between 1 and {MaxExpiresInDays}, or omitted for no expiration.", statusCode: StatusCodes.Status400BadRequest);
        }

        var expiresAt = request.ExpiresInDays is { } days ? timeProvider.GetUtcNow().AddDays(days) : (DateTimeOffset?)null;
        var (token, rawToken) = await tokens.CreateAsync(userId, request.Name, expiresAt, cancellationToken);
        var response = new CreateAccessTokenResponse { Token = ToDto(token, timeProvider), RawToken = rawToken };
        return ApiSerialization.Write(http, response, PersonalAccessTokensJsonContext.Default.CreateAccessTokenResponse, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAsync(HttpContext http, ClaimsPrincipal principal, IPersonalAccessTokenStore tokens, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        var list = await tokens.ListForUserAsync(userId, cancellationToken);
        var response = new AccessTokenListResponse { Tokens = list.Select(t => ToDto(t, timeProvider)).ToList() };
        return ApiSerialization.Write(http, response, PersonalAccessTokensJsonContext.Default.AccessTokenListResponse);
    }

    private static async Task<IResult> HandleRevokeAsync(Guid id, ClaimsPrincipal principal, IPersonalAccessTokenStore tokens, CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(principal, out var userId))
        {
            return Results.Unauthorized();
        }

        var token = await tokens.FindAsync(id, cancellationToken);
        if (token is null)
        {
            return Results.NotFound();
        }

        // Self-service, plus an Admin escape hatch - matches this file's "no admin
        // oversight *endpoint*" remark above (an Admin still can't list another user's
        // tokens) while giving an Admin a way to kill one specific leaked token they were
        // handed out-of-band, without resorting to disabling the whole account.
        if (token.UserId != userId && !principal.IsInRole(nameof(UserRole.Admin)))
        {
            return Results.Forbid();
        }

        await tokens.RevokeAsync(id, cancellationToken);
        return Results.NoContent();
    }

    private static bool TryGetCurrentUserId(ClaimsPrincipal principal, out Guid userId)
    {
        userId = Guid.Empty;
        // Same pattern as AuthEndpoints.HandleMeAsync: this only ever runs at all when
        // Flare's opt-in auth is enabled (see ConditionalAuthorizationMiddlewareResultHandler) -
        // when it's off there's no ClaimsPrincipal to read a user id from, and "a personal
        // access token for who?" has no sensible answer, so this correctly 401s instead.
        if (principal.Identity is not { IsAuthenticated: true })
        {
            return false;
        }

        var idClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return idClaim is not null && Guid.TryParse(idClaim, out userId);
    }

    private static AccessTokenDto ToDto(PersonalAccessToken token, TimeProvider timeProvider) => new()
    {
        Id = token.Id,
        Name = token.Name,
        CreatedAt = token.CreatedAt,
        ExpiresAt = token.ExpiresAt,
        LastUsedAt = token.LastUsedAt,
        RevokedAt = token.RevokedAt,
        IsActive = token.IsActive(timeProvider.GetUtcNow()),
    };
}
