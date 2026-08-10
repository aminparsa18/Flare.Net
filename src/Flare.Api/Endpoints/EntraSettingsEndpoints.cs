using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.Auth;

namespace Flare.Api.Endpoints;

/// <summary>
/// The Admin-only Security screen's backing API, under <c>/api/settings/entra</c> - lets
/// each self-hosted Flare operator paste in their own Entra App Registration's
/// Tenant/Client ID and secret (see docs/auth.md's "Microsoft Entra ID (SSO)" section),
/// mirroring Seq's own Security settings page rather than requiring a docker-compose/.env
/// edit. Saved values only take effect after a Flare.Api restart - see
/// <see cref="Auth.EntraOpenIdConnectOptionsConfigurator"/>'s remarks for why.
/// </summary>
public static class EntraSettingsEndpoints
{
    public static IEndpointRouteBuilder MapEntraSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/settings/entra", HandleGetAsync);
        endpoints.MapPut("/api/settings/entra", HandlePutAsync);
        return endpoints;
    }

    internal static async Task<IResult> HandleGetAsync(HttpContext http, IEntraSettingsStore entraSettings, CancellationToken cancellationToken)
    {
        var settings = await entraSettings.GetAsync(cancellationToken);
        return Results.Json(ToDto(settings, http), EntraSettingsJsonContext.Default.EntraSettingsDto);
    }

    internal static async Task<IResult> HandlePutAsync(HttpContext http, IEntraSettingsStore entraSettings, CancellationToken cancellationToken)
    {
        SaveEntraSettingsRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(http.Request.Body, EntraSettingsJsonContext.Default.SaveEntraSettingsRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Enabled)
        {
            if (string.IsNullOrWhiteSpace(request.TenantId) || string.IsNullOrWhiteSpace(request.ClientId))
            {
                return Results.Problem("Tenant ID and Client ID are required to enable Entra ID.", statusCode: StatusCodes.Status400BadRequest);
            }

            // A secret has to exist either from this call or a previous one - enabling
            // with neither would register a scheme Microsoft will always reject the
            // token exchange for.
            var existing = await entraSettings.GetAsync(cancellationToken);
            if (string.IsNullOrEmpty(request.ClientSecret) && string.IsNullOrEmpty(existing.ClientSecret))
            {
                return Results.Problem("A client secret is required to enable Entra ID.", statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var saved = await entraSettings.SaveAsync(request.Enabled, request.TenantId, request.ClientId, request.ClientSecret, cancellationToken);
        return Results.Json(ToDto(saved, http), EntraSettingsJsonContext.Default.EntraSettingsDto);
    }

    private static EntraSettingsDto ToDto(EntraSettings settings, HttpContext http) => new()
    {
        Enabled = settings.Enabled,
        TenantId = settings.TenantId,
        ClientId = settings.ClientId,
        HasClientSecret = !string.IsNullOrEmpty(settings.ClientSecret),
        RedirectUri = $"{http.Request.Scheme}://{http.Request.Host}/signin-oidc",
    };
}
