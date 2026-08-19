using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.Auth;

namespace Flare.Api.Endpoints;

/// <summary>
/// The consolidated <c>/auth</c> screen's Active Directory section, under
/// <c>/api/settings/ldap</c> - lets each self-hosted Flare operator point Flare at their
/// own AD/AD-compatible directory (see docs/auth.md's "Active Directory (LDAP)"
/// section). Unlike <see cref="EntraSettingsEndpoints"/>, saved values take effect
/// immediately - no ASP.NET Core authentication scheme/options caching is involved for
/// LDAP (see <see cref="LdapAuthEndpoints"/>'s remarks).
/// </summary>
public static class LdapSettingsEndpoints
{
    public static IEndpointRouteBuilder MapLdapSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/settings/ldap", HandleGetAsync);
        endpoints.MapPut("/api/settings/ldap", HandlePutAsync);
        return endpoints;
    }

    internal static async Task<IResult> HandleGetAsync(ILdapSettingsStore ldapSettings, CancellationToken cancellationToken)
    {
        var settings = await ldapSettings.GetAsync(cancellationToken);
        return Results.Json(ToDto(settings), LdapSettingsJsonContext.Default.LdapSettingsDto);
    }

    internal static async Task<IResult> HandlePutAsync(HttpContext http, ILdapSettingsStore ldapSettings, CancellationToken cancellationToken)
    {
        SaveLdapSettingsRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(http.Request.Body, LdapSettingsJsonContext.Default.SaveLdapSettingsRequest, cancellationToken);
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
            if (string.IsNullOrWhiteSpace(request.Host) || string.IsNullOrWhiteSpace(request.BaseDn) || string.IsNullOrWhiteSpace(request.BindDn))
            {
                return Results.Problem("Host, Base DN, and Bind DN are required to enable Active Directory.", statusCode: StatusCodes.Status400BadRequest);
            }

            // A bind password has to exist either from this call or a previous one -
            // enabling with neither would register a service account Flare can never
            // actually bind as.
            var existing = await ldapSettings.GetAsync(cancellationToken);
            if (string.IsNullOrEmpty(request.BindPassword) && string.IsNullOrEmpty(existing.BindPassword))
            {
                return Results.Problem("A bind password is required to enable Active Directory.", statusCode: StatusCodes.Status400BadRequest);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.PinnedCertificatePem))
        {
            try
            {
                using var _ = X509Certificate2.CreateFromPem(request.PinnedCertificatePem);
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException)
            {
                return Results.Problem("Pinned certificate must be a single, valid PEM-encoded X.509 certificate.", statusCode: StatusCodes.Status400BadRequest);
            }
        }

        var saved = await ldapSettings.SaveAsync(
            request.Enabled,
            request.Host,
            request.Port,
            request.UseSsl,
            request.PinnedCertificatePem,
            request.BaseDn,
            request.BindDn,
            request.BindPassword,
            request.UserSearchFilter,
            request.UniqueIdAttribute,
            request.AdminGroupDn,
            request.MemberGroupDn,
            request.ViewerGroupDn,
            request.DefaultRole,
            cancellationToken);
        return Results.Json(ToDto(saved), LdapSettingsJsonContext.Default.LdapSettingsDto);
    }

    private static LdapSettingsDto ToDto(LdapSettings settings) => new()
    {
        Enabled = settings.Enabled,
        Host = settings.Host,
        Port = settings.Port,
        UseSsl = settings.UseSsl,
        BaseDn = settings.BaseDn,
        BindDn = settings.BindDn,
        HasBindPassword = !string.IsNullOrEmpty(settings.BindPassword),
        UserSearchFilter = settings.UserSearchFilter,
        UniqueIdAttribute = settings.UniqueIdAttribute,
        AdminGroupDn = settings.AdminGroupDn,
        MemberGroupDn = settings.MemberGroupDn,
        ViewerGroupDn = settings.ViewerGroupDn,
        DefaultRole = settings.DefaultRole,
        PinnedCertificatePem = settings.PinnedCertificatePem,
    };
}
