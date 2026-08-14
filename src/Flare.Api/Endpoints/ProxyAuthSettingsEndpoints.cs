using System.Text.Json;
using Flare.Api.Auth;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.Auth;

namespace Flare.Api.Endpoints;

/// <summary>
/// The consolidated <c>/auth</c> screen's Reverse proxy section, under
/// <c>/api/settings/proxyauth</c> - lets each self-hosted Flare operator trust an
/// identity header from an already-authenticating reverse proxy in front of Flare (see
/// docs/auth.md's "Reverse proxy (trusted header)" section). Like
/// <see cref="LdapSettingsEndpoints"/>, saved values take effect immediately - no
/// ASP.NET Core authentication scheme/options caching is involved for this method.
/// </summary>
public static class ProxyAuthSettingsEndpoints
{
    public static IEndpointRouteBuilder MapProxyAuthSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/settings/proxyauth", HandleGetAsync);
        endpoints.MapPut("/api/settings/proxyauth", HandlePutAsync);
        return endpoints;
    }

    internal static async Task<IResult> HandleGetAsync(IProxyAuthSettingsStore proxySettings, CancellationToken cancellationToken)
    {
        var settings = await proxySettings.GetAsync(cancellationToken);
        return Results.Json(ToDto(settings), ProxyAuthJsonContext.Default.ProxyAuthSettingsDto);
    }

    internal static async Task<IResult> HandlePutAsync(HttpContext http, IProxyAuthSettingsStore proxySettings, CancellationToken cancellationToken)
    {
        SaveProxyAuthSettingsRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(http.Request.Body, ProxyAuthJsonContext.Default.SaveProxyAuthSettingsRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request.HeaderName))
        {
            return Results.Problem("Header name is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Enabled)
        {
            // The one method whose "enable" validation exists purely for safety, not
            // usability - a header is trivially spoofable by any client reaching
            // Flare.Api directly, so this can't be enabled with an empty/unparseable
            // trust boundary. See TrustedProxyNetworks' remarks.
            if (TrustedProxyNetworks.Parse(request.TrustedProxyCidrs).Count == 0)
            {
                return Results.Problem(
                    "At least one valid trusted proxy CIDR is required to enable reverse-proxy auth.",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var saved = await proxySettings.SaveAsync(
            request.Enabled,
            request.HeaderName,
            request.TrustedProxyCidrs,
            request.GroupsHeaderName,
            request.AdminGroup,
            request.MemberGroup,
            request.ViewerGroup,
            request.DefaultRole,
            cancellationToken);
        return Results.Json(ToDto(saved), ProxyAuthJsonContext.Default.ProxyAuthSettingsDto);
    }

    private static ProxyAuthSettingsDto ToDto(ProxyAuthSettings settings) => new()
    {
        Enabled = settings.Enabled,
        HeaderName = settings.HeaderName,
        TrustedProxyCidrs = settings.TrustedProxyCidrs,
        GroupsHeaderName = settings.GroupsHeaderName,
        AdminGroup = settings.AdminGroup,
        MemberGroup = settings.MemberGroup,
        ViewerGroup = settings.ViewerGroup,
        DefaultRole = settings.DefaultRole,
    };
}
