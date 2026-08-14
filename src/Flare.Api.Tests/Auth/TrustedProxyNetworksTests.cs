using System.Net;
using Flare.Api.Auth;
using Xunit;

namespace Flare.Api.Tests.Auth;

/// <summary>
/// Exercises <see cref="TrustedProxyNetworks"/> - the entire security boundary for
/// reverse-proxy auth, so its edge cases get particular attention: malformed entries,
/// out-of-range addresses, and the IPv4-mapped-IPv6 case Kestrel commonly reports for a
/// peer behind Docker's default bridge network (found documented the hard way in this
/// class' own remarks, not assumed).
/// </summary>
public class TrustedProxyNetworksTests
{
    [Fact]
    public void Parse_ReturnsOneNetwork_ForASingleValidCidr()
    {
        var networks = TrustedProxyNetworks.Parse("172.18.0.0/16");

        Assert.Single(networks);
    }

    [Fact]
    public void Parse_SplitsOnCommasAndNewlines_AndTrimsWhitespace()
    {
        var networks = TrustedProxyNetworks.Parse(" 172.18.0.0/16 , 10.0.0.0/8\n192.168.1.0/24 ");

        Assert.Equal(3, networks.Count);
    }

    [Fact]
    public void Parse_SkipsMalformedEntries_WithoutThrowing()
    {
        var networks = TrustedProxyNetworks.Parse("not-a-cidr, 172.18.0.0/16, also-bad");

        Assert.Single(networks);
    }

    [Fact]
    public void Parse_ReturnsEmpty_ForBlankInput()
    {
        Assert.Empty(TrustedProxyNetworks.Parse(""));
        Assert.Empty(TrustedProxyNetworks.Parse("   "));
    }

    [Fact]
    public void IsTrusted_ReturnsTrue_ForAnAddressInsideTheConfiguredRange()
    {
        Assert.True(TrustedProxyNetworks.IsTrusted(IPAddress.Parse("172.18.0.5"), "172.18.0.0/16"));
    }

    [Fact]
    public void IsTrusted_ReturnsFalse_ForAnAddressOutsideEveryConfiguredRange()
    {
        Assert.False(TrustedProxyNetworks.IsTrusted(IPAddress.Parse("203.0.113.7"), "172.18.0.0/16, 10.0.0.0/8"));
    }

    [Fact]
    public void IsTrusted_ReturnsFalse_ForANullRemoteIp()
    {
        Assert.False(TrustedProxyNetworks.IsTrusted(null, "172.18.0.0/16"));
    }

    [Fact]
    public void IsTrusted_NormalizesAnIPv4MappedIPv6Address_BeforeMatching()
    {
        // What Kestrel commonly reports for the direct TCP peer behind Docker's default
        // bridge network - an IPv4 CIDR entry must still match it, or every real
        // docker-compose deployment would silently reject every caller.
        var mapped = IPAddress.Parse("::ffff:172.18.0.5");

        Assert.True(TrustedProxyNetworks.IsTrusted(mapped, "172.18.0.0/16"));
    }

    [Fact]
    public void IsTrusted_MatchesExactSingleHostCidr()
    {
        Assert.True(TrustedProxyNetworks.IsTrusted(IPAddress.Parse("192.168.1.42"), "192.168.1.42/32"));
        Assert.False(TrustedProxyNetworks.IsTrusted(IPAddress.Parse("192.168.1.43"), "192.168.1.42/32"));
    }
}
