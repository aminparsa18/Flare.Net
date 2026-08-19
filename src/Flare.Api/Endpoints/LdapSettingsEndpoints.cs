using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Flare.Api.Auth;
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
            var parsed = new X509Certificate2Collection();
            try
            {
                parsed.ImportFromPem(request.PinnedCertificatePem);
            }
            catch (Exception ex) when (ex is CryptographicException or ArgumentException or FormatException)
            {
                return Results.Problem("Pinned certificate must be one or more valid PEM-encoded X.509 certificates.", statusCode: StatusCodes.Status400BadRequest);
            }

            try
            {
                if (parsed.Count == 0)
                {
                    return Results.Problem("Pinned certificate must be one or more valid PEM-encoded X.509 certificates.", statusCode: StatusCodes.Status400BadRequest);
                }

                // A bundle of only intermediate certificates has nothing for the chain
                // builder to terminate trust at - catch that here, at save time, with a
                // clear message, rather than letting it surface later as every LDAP login
                // silently failing (see LdapCertificateTrust.Validate's remarks).
                var hasTrustAnchor = false;
                foreach (var certificate in parsed)
                {
                    if (LdapCertificateTrust.IsTrustAnchor(certificate))
                    {
                        hasTrustAnchor = true;
                        break;
                    }
                }

                if (!hasTrustAnchor)
                {
                    return Results.Problem(
                        "Pinned certificate bundle must include at least one self-signed certificate (a root CA, or the domain controller's own certificate) to serve as a trust anchor - intermediate certificates alone can't be trusted.",
                        statusCode: StatusCodes.Status400BadRequest);
                }
            }
            finally
            {
                foreach (var certificate in parsed)
                {
                    certificate.Dispose();
                }
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
