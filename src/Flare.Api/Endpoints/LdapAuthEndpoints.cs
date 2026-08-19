using System.DirectoryServices.Protocols;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Flare.Api.Auth;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.Auth;
using Flare.Identity.Users;
using Microsoft.Extensions.Options;

namespace Flare.Api.Endpoints;

/// <summary>
/// Active Directory (LDAP) login, under <c>/api/auth/ldap</c> - unauthenticated by
/// design, same reasoning as <see cref="AuthEndpoints"/>. Unlike
/// <see cref="EntraAuthEndpoints"/>'s OIDC redirect dance, this is a single POST/JSON
/// endpoint shaped exactly like local login (see docs/auth.md's "Active Directory
/// (LDAP)" section for the full picture) - no ASP.NET Core authentication *scheme* is
/// registered for this, so (unlike Entra) settings changes take effect immediately, no
/// restart needed: <see cref="ILdapSettingsStore"/> is read fresh on every attempt, the
/// same way <see cref="IEntraSettingsStore"/> already is elsewhere.
///
/// Search-then-bind (confirmed with the user before building): bind once as a
/// configured service account to find the submitted username's real DN, then re-bind as
/// that DN with the submitted password to verify it - robust against real AD OU
/// structures, unlike a fragile direct-DN template.
/// </summary>
public static class LdapAuthEndpoints
{
    public static IEndpointRouteBuilder MapLdapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/ldap/login", HandleLoginAsync);
        return endpoints;
    }

    internal static async Task<IResult> HandleLoginAsync(
        HttpContext http,
        IUserStore users,
        ISessionStore sessions,
        ILdapSettingsStore ldapSettings,
        IOptions<AuthOptions> authOptions,
        CancellationToken cancellationToken)
    {
        var settings = await ldapSettings.GetAsync(cancellationToken);
        if (!settings.Enabled)
        {
            return Results.NotFound();
        }

        LoginRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(http.Request.Body, AuthJsonContext.Default.LoginRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrEmpty(request.Password))
        {
            return Results.Problem("Username and password are required.", statusCode: StatusCodes.Status400BadRequest);
        }

        FoundEntry? found;
        try
        {
            // Genuinely a Flare-side connectivity/config problem, not the caller's
            // credentials - 502, distinct from the 401 below, so an Admin debugging a
            // broken LDAP config isn't misled into thinking every login is a bad
            // password (same reasoning EntraOpenIdConnectOptionsConfigurator's remarks
            // give for a different failure mode).
            found = await Task.Run(() => FindUser(settings, request.Username), cancellationToken);
        }
        catch (LdapException)
        {
            return Results.Problem("Could not reach the LDAP server.", statusCode: StatusCodes.Status502BadGateway);
        }

        if (found is null)
        {
            // Unknown username - same generic 401 as a wrong password, below, both
            // mirroring IUserStore.VerifyPasswordAsync's documented anti-enumeration
            // stance (unknown username and wrong password must be indistinguishable).
            return Results.Unauthorized();
        }

        // A *separate* connection, deliberately - reusing the service-account
        // connection's own bind context would replace it, and this attempt failing
        // (wrong password) must not be conflated with the connectivity-problem 502
        // above: by this point Flare has already successfully reached the server, so
        // any failure here is about this one user's credentials, not Flare's config.
        var passwordCorrect = await Task.Run(() => VerifyPassword(settings, found.DistinguishedName, request.Password), cancellationToken);
        if (!passwordCorrect)
        {
            return Results.Unauthorized();
        }

        var user = await users.FindByExternalIdAsync("ActiveDirectory", found.ExternalId, cancellationToken);
        if (user is null)
        {
            var role = ResolveRole(found.MemberOf, settings);
            user = await users.CreateFromExternalAsync("ActiveDirectory", found.ExternalId, found.Username, role, cancellationToken);
        }

        if (user.IsDisabled)
        {
            return Results.Unauthorized();
        }

        await AuthEndpoints.SignInAsync(http, sessions, authOptions.Value, user, cancellationToken);
        return Results.Json(AuthEndpoints.ToDto(user), AuthJsonContext.Default.AuthUserDto);
    }

    /// <summary>Binds as the configured service account and searches for
    /// <paramref name="username"/> under <see cref="LdapSettings.BaseDn"/>. Runs
    /// synchronously (there's no async API in <c>System.DirectoryServices.Protocols</c>)
    /// - callers wrap this in <see cref="Task.Run(Action)"/> to avoid blocking a
    /// request thread on network I/O.</summary>
    private static FoundEntry? FindUser(LdapSettings settings, string username)
    {
        using var connection = CreateConnection(settings);
        connection.Bind(new NetworkCredential(settings.BindDn, settings.BindPassword));

        var escapedUsername = LdapFilterEncoder.Escape(username);
        var filter = string.Format(settings.UserSearchFilter, escapedUsername);
        var request = new SearchRequest(settings.BaseDn, filter, SearchScope.Subtree, settings.UniqueIdAttribute, "memberOf");
        var response = (SearchResponse)connection.SendRequest(request);

        if (response.Entries.Count == 0)
        {
            return null;
        }

        var entry = response.Entries[0];
        var externalId = ReadUniqueId(entry, settings.UniqueIdAttribute);
        if (externalId is null)
        {
            return null;
        }

        var memberOf = entry.Attributes.Contains("memberOf")
            ? entry.Attributes["memberOf"].GetValues(typeof(string)).Cast<string>().ToArray()
            : [];

        return new FoundEntry(entry.DistinguishedName, username, externalId, memberOf);
    }

    /// <summary>Re-binds as <paramref name="distinguishedName"/> with the submitted
    /// password, on a fresh connection - the actual credential check. Any bind failure
    /// (wrong password, disabled/locked account, ...) is reported the same way: false,
    /// never an exception the caller needs to distinguish further.</summary>
    private static bool VerifyPassword(LdapSettings settings, string distinguishedName, string password)
    {
        try
        {
            using var connection = CreateConnection(settings);
            connection.Bind(new NetworkCredential(distinguishedName, password));
            return true;
        }
        catch (LdapException)
        {
            return false;
        }
    }

    private static LdapConnection CreateConnection(LdapSettings settings)
    {
        var connection = new LdapConnection(new LdapDirectoryIdentifier(settings.Host, settings.Port))
        {
            AuthType = AuthType.Basic,
            Timeout = TimeSpan.FromSeconds(10),
        };
        connection.SessionOptions.ProtocolVersion = 3;
        connection.SessionOptions.SecureSocketLayer = settings.UseSsl;

        if (settings.UseSsl && !string.IsNullOrWhiteSpace(settings.PinnedCertificatePem))
        {
            // Certificate pinning (docs/auth.md's "Active Directory (LDAP)" section) -
            // bypasses the OS/container trust store for this connection in favor of
            // trusting only the Admin-configured certificate. Re-parsed per connection:
            // connections here are already created-and-disposed per FindUser/VerifyPassword
            // call, no caching layer, consistent with settings themselves being re-read
            // fresh on every attempt (see this type's own class remarks). A malformed PEM
            // at this point (e.g. a hand-edited DB row bypassing LdapSettingsEndpoints'
            // save-time validation) is deliberately left to throw rather than silently
            // falling back to the OS trust store.
            var pinnedCertificate = X509Certificate2.CreateFromPem(settings.PinnedCertificatePem);
            connection.SessionOptions.VerifyServerCertificate = (_, certificate) =>
                LdapCertificateTrust.Validate(pinnedCertificate, certificate);
        }

        return connection;
    }

    /// <summary>AD's <c>objectGUID</c> (the default <see cref="LdapSettings.UniqueIdAttribute"/>)
    /// comes back as raw bytes; other directories' equivalent (e.g. OpenLDAP's
    /// <c>entryUUID</c>, used to verify this code's LDAP wire mechanics against a real
    /// (non-AD) server - see docs/auth.md) return an already-formatted string. Handling
    /// both keeps the configured attribute name meaningful either way, without a second
    /// "is this attribute binary" setting to configure.</summary>
    private static string? ReadUniqueId(SearchResultEntry entry, string attributeName)
    {
        if (!entry.Attributes.Contains(attributeName))
        {
            return null;
        }

        var value = entry.Attributes[attributeName][0];
        return value switch
        {
            byte[] bytes => new Guid(bytes).ToString(),
            string text => text,
            _ => value.ToString(),
        };
    }

    /// <summary>Highest-privilege matching group DN (Admin > Member > Viewer), or
    /// <see cref="LdapSettings.DefaultRole"/> if <paramref name="memberOf"/> matches
    /// none of the three configured group DNs - mirrors
    /// <see cref="EntraAuthEndpoints.ResolveRole"/>'s same precedence over Entra App
    /// Roles. DN comparison is ordinal-case-insensitive - LDAP DNs are conventionally
    /// case-insensitive on their attribute names/values, and directories commonly
    /// normalize casing differently than however an Admin typed a group DN into the
    /// dashboard.</summary>
    internal static UserRole ResolveRole(IReadOnlyCollection<string> memberOf, LdapSettings settings)
    {
        bool IsMember(string? groupDn) => groupDn is not null && memberOf.Contains(groupDn, StringComparer.OrdinalIgnoreCase);

        if (IsMember(settings.AdminGroupDn))
        {
            return UserRole.Admin;
        }
        if (IsMember(settings.MemberGroupDn))
        {
            return UserRole.Member;
        }
        if (IsMember(settings.ViewerGroupDn))
        {
            return UserRole.Viewer;
        }
        return settings.DefaultRole;
    }

    private sealed record FoundEntry(string DistinguishedName, string Username, string ExternalId, IReadOnlyCollection<string> MemberOf);
}
