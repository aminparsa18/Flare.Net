using System.Net;

namespace Flare.Api.Auth;

/// <summary>
/// Parses <see cref="Identity.Auth.ProxyAuthSettings.TrustedProxyCidrs"/>' raw
/// newline/comma-separated string into <see cref="IPNetwork"/> entries (the BCL type,
/// .NET 8+ - no new package needed) and tests whether a caller's remote IP falls inside
/// any of them. This is the entire security boundary for reverse-proxy auth: a header
/// can be trivially spoofed by any client that can reach <c>Flare.Api</c> directly, so
/// <see cref="Endpoints.ProxyAuthLoginEndpoints"/> only trusts the configured header when
/// the request's own TCP peer - not a forwarded header, which would itself be spoofable -
/// is inside one of these networks. Deliberately does not read
/// <c>X-Forwarded-For</c>/use <c>UseForwardedHeaders()</c>: trusting a spoofable header to
/// establish trust for a *different* spoofable header would defeat the entire point.
/// </summary>
public static class TrustedProxyNetworks
{
    /// <summary>Parses every non-blank entry; malformed entries are silently skipped
    /// (the caller - <see cref="Endpoints.ProxyAuthSettingsEndpoints"/> - is responsible
    /// for rejecting a save that produces zero usable entries when enabling).</summary>
    public static IReadOnlyList<IPNetwork> Parse(string trustedProxyCidrs)
    {
        var networks = new List<IPNetwork>();
        foreach (var entry in trustedProxyCidrs.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IPNetwork.TryParse(entry, out var network))
            {
                networks.Add(network);
            }
        }
        return networks;
    }

    /// <summary>True if <paramref name="remoteIp"/> falls inside any parsed entry from
    /// <paramref name="trustedProxyCidrs"/>. Normalizes an IPv4-mapped-IPv6 address
    /// (<c>::ffff:172.18.0.3</c>, what Kestrel commonly reports for the peer behind
    /// Docker's default bridge network) to plain IPv4 first - found the hard way: an
    /// IPv4 CIDR entry never matches the mapped form otherwise, silently rejecting every
    /// caller.</summary>
    public static bool IsTrusted(IPAddress? remoteIp, string trustedProxyCidrs)
    {
        if (remoteIp is null)
        {
            return false;
        }

        var normalized = remoteIp.IsIPv4MappedToIPv6 ? remoteIp.MapToIPv4() : remoteIp;
        return Parse(trustedProxyCidrs).Any(network => network.Contains(normalized));
    }
}
