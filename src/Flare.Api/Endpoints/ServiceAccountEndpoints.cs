using System.Text.Json;
using Flare.Api.Auditing;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.PersonalAccessTokens;
using Flare.Identity.Users;

namespace Flare.Api.Endpoints;

/// <summary>
/// Admin-only management of service accounts (ADR-0082): non-human principals for CI, scripts
/// and shared integrations (including the <c>/mcp</c> endpoint) that must not depend on a
/// person's account. A service account is a <see cref="User"/> whose AuthProvider is
/// "ServiceAccount", so listing, role changes and disabling reuse <c>/api/users</c>, and its
/// tokens are ordinary personal access tokens revoked through <c>DELETE /api/access-tokens/{id}</c>
/// (Admins may revoke anyone's). What's added here is creation and Admin-issued tokens: a
/// service account can't mint its own (see PersonalAccessTokenEndpoints), so every issue or
/// rotation lands in the audit log under a named Admin.
/// </summary>
public static class ServiceAccountEndpoints
{
    private const int MaxNameLength = 64;

    public static IEndpointRouteBuilder MapServiceAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/service-accounts", HandleCreateAsync);
        endpoints.MapGet("/api/service-accounts/{id:guid}", HandleGetAsync);
        endpoints.MapDelete("/api/service-accounts/{id:guid}", HandleDeleteAsync);
        endpoints.MapPost("/api/service-accounts/{id:guid}/access-tokens", HandleCreateTokenAsync);
        endpoints.MapGet("/api/service-accounts/{id:guid}/access-tokens", HandleListTokensAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, IUserStore users, CancellationToken cancellationToken)
    {
        CreateServiceAccountRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, UsersJsonContext.Default.CreateServiceAccountRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        var name = request?.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > MaxNameLength)
        {
            return Results.Problem($"Name is required and at most {MaxNameLength} characters.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (await users.FindByUsernameAsync(name, cancellationToken) is not null)
        {
            return Results.Problem($"An account named '{name}' already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        var account = await users.CreateServiceAccountAsync(name, request!.Role, cancellationToken);
        AuditContext.SetResourceId(http, account.Id);
        return ApiSerialization.Write(http, ToDto(account), UsersJsonContext.Default.UserSummaryDto, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleGetAsync(Guid id, HttpContext http, IUserStore users, CancellationToken cancellationToken)
    {
        var account = await users.FindByIdAsync(id, cancellationToken);
        return account is { IsServiceAccount: true }
            ? ApiSerialization.Write(http, ToDto(account), UsersJsonContext.Default.UserSummaryDto)
            : Results.NotFound();
    }

    /// <summary>Permanent: the account, its tokens and its memberships go. To keep the account but stop it
    /// authenticating, disable it through <c>PATCH /api/users/{id}/disabled</c> instead.</summary>
    private static async Task<IResult> HandleDeleteAsync(Guid id, HttpContext http, IUserStore users, CancellationToken cancellationToken)
    {
        var account = await users.FindByIdAsync(id, cancellationToken);
        if (account is not { IsServiceAccount: true } || !await users.DeleteServiceAccountAsync(id, cancellationToken))
        {
            return Results.NotFound();
        }

        AuditContext.SetResourceId(http, id);
        return Results.NoContent();
    }

    private static async Task<IResult> HandleCreateTokenAsync(
        Guid id, HttpContext http, IUserStore users, IPersonalAccessTokenStore tokens, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var account = await users.FindByIdAsync(id, cancellationToken);
        if (account is not { IsServiceAccount: true })
        {
            return Results.NotFound();
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

        if (request.ExpiresInDays is < 1 or > PersonalAccessTokenEndpoints.MaxExpiresInDays)
        {
            return Results.Problem($"ExpiresInDays must be between 1 and {PersonalAccessTokenEndpoints.MaxExpiresInDays}, or omitted for no expiration.", statusCode: StatusCodes.Status400BadRequest);
        }

        var expiresAt = request.ExpiresInDays is { } days ? timeProvider.GetUtcNow().AddDays(days) : (DateTimeOffset?)null;
        var (token, rawToken) = await tokens.CreateAsync(account.Id, request.Name, expiresAt, cancellationToken);
        AuditContext.SetResourceId(http, account.Id);
        var response = new CreateAccessTokenResponse { Token = PersonalAccessTokenEndpoints.ToDto(token, timeProvider), RawToken = rawToken };
        return ApiSerialization.Write(http, response, PersonalAccessTokensJsonContext.Default.CreateAccessTokenResponse, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListTokensAsync(
        Guid id, HttpContext http, IUserStore users, IPersonalAccessTokenStore tokens, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var account = await users.FindByIdAsync(id, cancellationToken);
        if (account is not { IsServiceAccount: true })
        {
            return Results.NotFound();
        }

        var list = await tokens.ListForUserAsync(account.Id, cancellationToken);
        var response = new AccessTokenListResponse { Tokens = list.Select(t => PersonalAccessTokenEndpoints.ToDto(t, timeProvider)).ToList() };
        return ApiSerialization.Write(http, response, PersonalAccessTokensJsonContext.Default.AccessTokenListResponse);
    }

    private static UserSummaryDto ToDto(User user) => new()
    {
        Id = user.Id,
        Username = user.Username,
        Role = user.Role,
        AuthProvider = user.AuthProvider,
        IsDisabled = user.IsDisabled,
        CreatedAt = user.CreatedAt,
    };
}
