using Flare.Identity.Users;

namespace Flare.Api.Model;

/// <summary>Response body for <c>GET /api/settings/proxyauth</c> - the consolidated
/// <c>/auth</c> screen's Reverse proxy section. No secret field to mask - unlike
/// <see cref="EntraSettingsDto"/>/<see cref="LdapSettingsDto"/>/<see cref="OidcSettingsDto"/>,
/// this is a plain round-trip of everything stored.</summary>
public sealed record ProxyAuthSettingsDto
{
    public required bool Enabled { get; init; }

    public required string HeaderName { get; init; }

    public required string TrustedProxyCidrs { get; init; }

    public string? GroupsHeaderName { get; init; }

    public string? AdminGroup { get; init; }

    public string? MemberGroup { get; init; }

    public string? ViewerGroup { get; init; }

    public required UserRole DefaultRole { get; init; }

    /// <summary>Optional. When set, <c>/api/auth/logout</c> sends the browser here after
    /// clearing the local session, for <c>ReverseProxy</c>-provisioned accounts only -
    /// see docs/auth.md's "Known limitations" for why Flare can't propagate logout to
    /// the proxy/IdP automatically. Null means today's unchanged behavior: back to
    /// <c>/login</c>.</summary>
    public string? LogoutRedirectUrl { get; init; }
}

/// <summary>Request body for <c>PUT /api/settings/proxyauth</c>.</summary>
public sealed record SaveProxyAuthSettingsRequest
{
    public required bool Enabled { get; init; }

    public required string HeaderName { get; init; }

    public required string TrustedProxyCidrs { get; init; }

    public string? GroupsHeaderName { get; init; }

    public string? AdminGroup { get; init; }

    public string? MemberGroup { get; init; }

    public string? ViewerGroup { get; init; }

    public required UserRole DefaultRole { get; init; }

    public string? LogoutRedirectUrl { get; init; }
}
