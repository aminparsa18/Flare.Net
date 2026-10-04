using Flare.Api.Auth;
using MimeKit;
using System.Text.Json;
using Flare.Api.Auditing;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.Auth;
using Flare.Identity.PasswordSetTokens;
using Flare.Identity.Users;

namespace Flare.Api.Endpoints;

/// <summary>
/// Manages user accounts (local and Entra-provisioned alike), under <c>/api/users</c> -
/// Admin-only (see <c>Program.cs</c>'s <c>RequireAdmin</c> policy group, same group
/// <see cref="IngestApiKeyEndpoints"/> uses). Thin wrappers over
/// <see cref="IUserStore.ListAsync"/>/<see cref="IUserStore.SetRoleAsync"/>/
/// <see cref="IUserStore.SetDisabledAsync"/> - those already existed (needed by the local-
/// account design from the start) but had no caller until this file, since v1 shipped
/// with only the single first-run bootstrap Admin and no way to manage anyone else. Entra
/// auto-provisioning is what actually forces this gap closed: a newly-provisioned Viewer
/// needs a path to Member/Admin that isn't hand-editing SQLite (see docs/auth.md).
/// </summary>
public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users", HandleListAsync);
        endpoints.MapPatch("/api/users/{id:guid}/role", HandleSetRoleAsync);
        endpoints.MapPatch("/api/users/{id:guid}/disabled", HandleSetDisabledAsync);
        endpoints.MapPost("/api/users/invite", HandleInviteAsync);
        endpoints.MapPost("/api/users/{id:guid}/password-reset", HandlePasswordResetAsync);
        return endpoints;
    }

    internal static readonly TimeSpan InviteLifetime = TimeSpan.FromDays(3);
    internal static readonly TimeSpan ResetLifetime = TimeSpan.FromHours(24);

    /// <summary>Creates a local account whose password nobody knows (random, discarded) and
    /// returns a one-time set-password token for the admin to hand over.</summary>
    internal static async Task<IResult> HandleInviteAsync(
        HttpContext http, IUserStore users, IPasswordSetTokenStore tokens, IAuthSettingsStore authSettings, IPasswordResetMailer mailer, CancellationToken cancellationToken)
    {
        if (!(await authSettings.GetAsync(cancellationToken)).LocalEnabled)
        {
            return Results.Problem("Local accounts are disabled.", statusCode: StatusCodes.Status400BadRequest);
        }

        InviteUserRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, UsersJsonContext.Default.InviteUserRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Username))
        {
            return Results.Problem("Username is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var username = request.Username.Trim();
        if (await users.FindByUsernameAsync(username, cancellationToken) is not null)
        {
            return Results.Problem("A user with that username already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        var user = await users.CreateAsync(username, $"{Guid.NewGuid():N}{Guid.NewGuid():N}", request.Role, cancellationToken);
        var token = await tokens.CreateAsync(user.Id, PasswordSetPurpose.Invite, InviteLifetime, cancellationToken);
        // The username is the address (ADR-0113); the raw link is still returned so the admin can copy it.
        var emailed = mailer.IsConfigured
            && username.Contains('@')
            && MailboxAddress.TryParse(username, out _)
            && await mailer.SendAsync(username, mailer.BuildLink(token.RawToken), cancellationToken, invite: true);
        return ApiSerialization.Write(
            http,
            new PasswordSetLinkResponse { User = ToDto(user), Token = token.RawToken, ExpiresAt = token.ExpiresAt, EmailSent = emailed },
            UsersJsonContext.Default.PasswordSetLinkResponse,
            statusCode: StatusCodes.Status201Created);
    }

    /// <summary>Admin-generated reset link for a local account. Revokes the user's sessions
    /// immediately - a reset is usually "I'm locked out" or "this account may be compromised".</summary>
    internal static async Task<IResult> HandlePasswordResetAsync(
        Guid id, HttpContext http, IUserStore users, ISessionStore sessions, IPasswordSetTokenStore tokens, CancellationToken cancellationToken)
    {
        var target = await users.FindByIdAsync(id, cancellationToken);
        if (target is null)
        {
            return Results.NotFound();
        }

        if (target.AuthProvider != "Local")
        {
            return Results.Problem("Only local accounts have a Flare-managed password.", statusCode: StatusCodes.Status400BadRequest);
        }

        var token = await tokens.CreateAsync(target.Id, PasswordSetPurpose.Reset, ResetLifetime, cancellationToken);
        await sessions.DeleteAllForUserAsync(target.Id, cancellationToken);
        return ApiSerialization.Write(
            http,
            new PasswordSetLinkResponse { User = ToDto(target), Token = token.RawToken, ExpiresAt = token.ExpiresAt },
            UsersJsonContext.Default.PasswordSetLinkResponse);
    }

    internal static async Task<IResult> HandleListAsync(HttpContext http, IUserStore users, CancellationToken cancellationToken)
    {
        var list = await users.ListAsync(cancellationToken);
        var response = new UserListResponse { Users = list.Select(ToDto).ToList() };
        return ApiSerialization.Write(http, response, UsersJsonContext.Default.UserListResponse);
    }

    internal static async Task<IResult> HandleSetRoleAsync(Guid id, HttpContext http, IUserStore users, CancellationToken cancellationToken)
    {
        SetUserRoleRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, UsersJsonContext.Default.SetUserRoleRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var target = await users.FindByIdAsync(id, cancellationToken);
        if (target is null)
        {
            return Results.NotFound();
        }

        if (target.Role == UserRole.Admin && request.Role != UserRole.Admin
            && await IsLastEnabledAdminAsync(users, target.Id, cancellationToken))
        {
            return Results.Problem("Can't remove the last enabled Admin.", statusCode: StatusCodes.Status400BadRequest);
        }

        await users.SetRoleAsync(id, request.Role, cancellationToken);
        var updated = await users.FindByIdAsync(id, cancellationToken);
        AuditContext.SetChange(http, UsersJsonContext.Default.UserSummaryDto, ToDto(target), ToDto(updated!));
        return ApiSerialization.Write(http, ToDto(updated!), UsersJsonContext.Default.UserSummaryDto);
    }

    internal static async Task<IResult> HandleSetDisabledAsync(Guid id, HttpContext http, IUserStore users, CancellationToken cancellationToken)
    {
        SetUserDisabledRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, UsersJsonContext.Default.SetUserDisabledRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var target = await users.FindByIdAsync(id, cancellationToken);
        if (target is null)
        {
            return Results.NotFound();
        }

        if (request.IsDisabled && target.Role == UserRole.Admin
            && await IsLastEnabledAdminAsync(users, target.Id, cancellationToken))
        {
            return Results.Problem("Can't disable the last enabled Admin.", statusCode: StatusCodes.Status400BadRequest);
        }

        await users.SetDisabledAsync(id, request.IsDisabled, cancellationToken);
        var updated = await users.FindByIdAsync(id, cancellationToken);
        AuditContext.SetChange(http, UsersJsonContext.Default.UserSummaryDto, ToDto(target), ToDto(updated!));
        return ApiSerialization.Write(http, ToDto(updated!), UsersJsonContext.Default.UserSummaryDto);
    }

    /// <summary>True if <paramref name="excludingId"/> is (or is about to stop being) the
    /// only enabled Admin left - the guard both mutating endpoints above use to avoid an
    /// admin-lockout only recoverable by editing SQLite directly. Reads the full user list
    /// rather than a dedicated COUNT query - this table is small (a handful to a few
    /// hundred rows for a self-hosted single-instance tool) and Admin-only endpoints are
    /// not a hot path, so the extra simplicity wins over a second IUserStore method.</summary>
    private static async Task<bool> IsLastEnabledAdminAsync(IUserStore users, Guid excludingId, CancellationToken cancellationToken)
    {
        var all = await users.ListAsync(cancellationToken);
        return !all.Any(u => u.Id != excludingId && u.Role == UserRole.Admin && !u.IsDisabled);
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
