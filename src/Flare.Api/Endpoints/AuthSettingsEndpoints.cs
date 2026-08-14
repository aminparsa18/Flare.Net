using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.Auth;

namespace Flare.Api.Endpoints;

/// <summary>
/// The consolidated <c>/auth</c> screen's umbrella on/off switch, under
/// <c>/api/settings/auth</c>. Mapped onto the same <c>adminRoutes</c> group as every
/// other Admin-only endpoint in <c>Program.cs</c> - deliberately, not a self-guarding
/// endpoint like <see cref="EntraAuthEndpoints"/>'s disabled-gate checks: when
/// <see cref="AuthSettings.Enabled"/> is false, <c>ConditionalAuthorizationMiddlewareResultHandler</c>
/// already bypasses the <c>RequireAdmin</c> check for *every* Admin-only route including
/// this one, so anyone can reach this endpoint to turn auth on in the first place; once
/// <c>Enabled</c> is true, the same policy re-engages and only an Admin can change it
/// (or turn it back off) - no extra code needed here to get that behavior.
/// </summary>
public static class AuthSettingsEndpoints
{
    public static IEndpointRouteBuilder MapAuthSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/settings/auth", HandleGetAsync);
        endpoints.MapPut("/api/settings/auth", HandlePutAsync);
        return endpoints;
    }

    internal static async Task<IResult> HandleGetAsync(IAuthSettingsStore authSettings, CancellationToken cancellationToken)
    {
        var settings = await authSettings.GetAsync(cancellationToken);
        return Results.Json(ToDto(settings), AuthSettingsJsonContext.Default.AuthSettingsDto);
    }

    internal static async Task<IResult> HandlePutAsync(
        HttpContext http,
        IAuthSettingsStore authSettings,
        IEntraSettingsStore entraSettings,
        ILdapSettingsStore ldapSettings,
        IOidcSettingsStore oidcSettings,
        CancellationToken cancellationToken)
    {
        AuthSettingsDto? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(http.Request.Body, AuthSettingsJsonContext.Default.AuthSettingsDto, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Enabled && !request.LocalEnabled)
        {
            // Turning global auth on with no usable method would be a total lockout -
            // nobody could ever sign in to manage it back.
            var entra = await entraSettings.GetAsync(cancellationToken);
            var ldap = await ldapSettings.GetAsync(cancellationToken);
            var oidc = await oidcSettings.GetAsync(cancellationToken);
            if (!entra.Enabled && !ldap.Enabled && !oidc.Enabled)
            {
                return Results.Problem(
                    "At least one sign-in method (local, Entra ID, Active Directory, or OpenID Connect) must be enabled before turning authentication on.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var saved = await authSettings.SaveAsync(request.Enabled, request.LocalEnabled, cancellationToken);
        return Results.Json(ToDto(saved), AuthSettingsJsonContext.Default.AuthSettingsDto);
    }

    private static AuthSettingsDto ToDto(AuthSettings settings) => new() { Enabled = settings.Enabled, LocalEnabled = settings.LocalEnabled };
}
