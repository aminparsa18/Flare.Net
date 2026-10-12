using System.Net;
using System.Net.Sockets;

namespace Flare.ServiceDefaults.Outbound;

/// <summary>Decides whether a connection to a resolved address is allowed. Pure, so it is unit-tested without sockets.</summary>
public sealed class OutboundAddressPolicy
{
    private readonly bool _block;
    private readonly List<IPNetwork> _allowNetworks = [];
    private readonly List<string> _allowHosts = [];

    public OutboundAddressPolicy(OutboundGuardOptions options)
    {
        _block = options.BlockPrivateNetworks;
        foreach (var raw in options.Allow)
        {
            var entry = raw.Trim();
            if (entry.Length == 0)
            {
                continue;
            }

            if (IPNetwork.TryParse(entry, out var network))
            {
                _allowNetworks.Add(network);
            }
            else if (IPAddress.TryParse(entry, out var ip))
            {
                _allowNetworks.Add(new IPNetwork(ip, ip.AddressFamily == AddressFamily.InterNetwork ? 32 : 128));
            }
            else
            {
                _allowHosts.Add(entry.ToLowerInvariant());
            }
        }
    }

    public bool Enabled => _block;

    /// <summary>True when connecting to <paramref name="address"/> (reached through <paramref name="host"/>) is permitted.</summary>
    public bool IsAllowed(string host, IPAddress address)
    {
        if (!_block || !IsBlocked(address))
        {
            return true;
        }

        var normalized = Unwrap(address);
        foreach (var network in _allowNetworks)
        {
            if (network.Contains(normalized) || network.Contains(address))
            {
                return true;
            }
        }

        var h = host.TrimEnd('.').ToLowerInvariant();
        foreach (var allowed in _allowHosts)
        {
            if (allowed.StartsWith("*.", StringComparison.Ordinal)
                ? h.EndsWith(allowed[1..], StringComparison.Ordinal)
                : h == allowed)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Resolves <paramref name="host"/> and throws if any answer is refused. Any blocked answer rejects the whole name: a
    /// mixed answer is how a rebinding attack gets a private address past a "first address" check.
    /// </summary>
    public async Task<IPAddress[]> ResolveCheckedAsync(string host, CancellationToken ct)
    {
        var addresses = IPAddress.TryParse(host, out var literal) ? [literal] : await Dns.GetHostAddressesAsync(host, ct);
        if (addresses.FirstOrDefault(a => !IsAllowed(host, a)) is { } denied)
        {
            throw new HttpRequestException(
                $"Blocked outbound request to {host} ({denied}): private, loopback and link-local destinations are refused. " +
                "Add the host or network to Outbound__Allow, or set Outbound__BlockPrivateNetworks=false.");
        }

        return addresses;
    }

    /// <summary>The refusal message for <paramref name="url"/> as it would be sent today, or null when it is allowed (or not an http(s) URL / not resolvable).</summary>
    public async Task<string?> CheckUrlAsync(string? url, CancellationToken ct)
    {
        if (!_block || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        try
        {
            await ResolveCheckedAsync(uri.DnsSafeHost, ct);
            return null;
        }
        catch (HttpRequestException ex)
        {
            return ex.Message;
        }
        catch (System.Net.Sockets.SocketException)
        {
            return null; // unresolvable is the send-test's problem, not a policy refusal
        }
    }

    /// <summary>True for any address that is not a routable public unicast address.</summary>
    public static bool IsBlocked(IPAddress address)
    {
        address = Unwrap(address);
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) || address.Equals(IPAddress.Broadcast))
        {
            return true;
        }

        var b = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return b[0] == 0                                        // 0.0.0.0/8 "this network"
                || b[0] == 10                                       // RFC 1918
                || (b[0] == 100 && (b[1] & 0xC0) == 64)             // 100.64.0.0/10 CGNAT (some clouds' internal range)
                || (b[0] == 169 && b[1] == 254)                     // link-local, incl. 169.254.169.254 metadata
                || (b[0] == 172 && (b[1] & 0xF0) == 16)             // RFC 1918
                || (b[0] == 192 && b[1] == 0 && b[2] == 0)          // 192.0.0.0/24 IETF protocol assignments
                || (b[0] == 192 && b[1] == 168)                     // RFC 1918
                || (b[0] == 198 && (b[1] & 0xFE) == 18)             // 198.18.0.0/15 benchmarking
                || b[0] >= 224;                                     // multicast + reserved
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (b[0] & 0xFE) == 0xFC)
            {
                return true; // fe80::/10, fec0::/10, ff00::/8, fc00::/7 (ULA)
            }

            // NAT64 64:ff9b::/96 embeds an IPv4 address the translator will connect to.
            if (b[0] == 0x00 && b[1] == 0x64 && b[2] == 0xFF && b[3] == 0x9B && b[4..12].All(x => x == 0))
            {
                return IsBlocked(new IPAddress(b[12..16]));
            }

            return false;
        }

        return true;
    }

    private static IPAddress Unwrap(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}
