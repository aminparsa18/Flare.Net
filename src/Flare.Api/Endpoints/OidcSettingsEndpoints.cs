using System.Text.Json;
using Flare.Api.Auth;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.Auth;

namespace Flare.Api.Endpoints;

/// <summary>
/// The consolidated <c>/auth</c> screen's generic OpenID Connect section, under
/// <c>/api/settings/oidc</c> - lets each self-hosted Flare operator point Flare at any
/// standards-compliant OIDC provider (see docs/auth.md's "OpenID Connect" section),
/// mirroring Seq's own Security settings page. Saved values only take effect after a
/// Flare.Api restart - see <see cref="Auth.OidcOpenIdConnectOptionsConfigurator"/>'s
/// remarks for why, same as <see cref="EntraSettingsEndpoints"/>.
/// </summary>
public static class OidcSettingsEndpoints
{
    public static IEndpointRouteBuilder MapOidcSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/settings/oidc", HandleGetAsync);
        endpoints.MapPut("/api/settings/oidc", HandlePutAsync);
        return endpoints;
    }

    internal static async Task<IResult> HandleGetAsync(HttpContext http, IOidcSettingsStore oidcSettings, CancellationToken cancellationToken)
    {
        var settings = await oidcSettings.GetAsync(cancellationToken);
        return ApiSerialization.Write(http, ToDto(settings, http), OidcSettingsJsonContext.Default.OidcSettingsDto);
    }

    internal static async Task<IResult> HandlePutAsync(HttpContext http, IOidcSettingsStore oidcSettings, CancellationToken cancellationToken)
    {
        SaveOidcSettingsRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, OidcSettingsJsonContext.Default.SaveOidcSettingsRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.Scopes))
        {
            return Results.Problem("Scopes is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.RoleClaimName))
        {
            return Results.Problem("Role claim name is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Enabled)
        {
            if (string.IsNullOrWhiteSpace(request.Authority) || string.IsNullOrWhiteSpace(request.ClientId))
            {
                return Results.Problem("Authority and Client ID are required to enable OpenID Connect.", statusCode: StatusCodes.Status400BadRequest);
            }

            // A secret has to exist either from this call or a previous one - enabling
            // with neither would register a scheme the provider will always reject the
            // token exchange for.
            var existing = await oidcSettings.GetAsync(cancellationToken);
            if (string.IsNullOrEmpty(request.ClientSecret) && string.IsNullOrEmpty(existing.ClientSecret))
            {
                return Results.Problem("A client secret is required to enable OpenID Connect.", statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var saved = await oidcSettings.SaveAsync(
            request.Enabled,
            request.DisplayName,
            request.Authority,
            request.ClientId,
            request.ClientSecret,
            request.Scopes,
            request.RoleClaimName,
            request.DefaultRole,
            cancellationToken);
        return ApiSerialization.Write(http, ToDto(saved, http), OidcSettingsJsonContext.Default.OidcSettingsDto);
    }

    private static OidcSettingsDto ToDto(OidcSettings settings, HttpContext http) => new()
    {
        Enabled = settings.Enabled,
        DisplayName = settings.DisplayName,
        Authority = settings.Authority,
        ClientId = settings.ClientId,
        HasClientSecret = !string.IsNullOrEmpty(settings.ClientSecret),
        Scopes = settings.Scopes,
        RoleClaimName = settings.RoleClaimName,
        DefaultRole = settings.DefaultRole,
        RedirectUri = $"{http.Request.Scheme}://{http.Request.Host}{OidcAuthenticationDefaults.CallbackPath}",
    };
}
