using Xunit;
using System.Net;
using Flare.ServiceDefaults.Outbound;
using Microsoft.Extensions.DependencyInjection;

namespace Flare.Api.Tests.Outbound;

public class OutboundAddressPolicyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fe80::1")]
    [InlineData("fd00::1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("64:ff9b::a00:1")]
    public void IsBlocked_non_public_addresses(string ip) => Assert.True(OutboundAddressPolicy.IsBlocked(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.15.0.1")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700:4700::1111")]
    [InlineData("::ffff:8.8.8.8")]
    public void IsBlocked_public_addresses_pass(string ip) => Assert.False(OutboundAddressPolicy.IsBlocked(IPAddress.Parse(ip)));

    [Fact]
    public void Disabled_allows_everything()
    {
        var policy = new OutboundAddressPolicy(new OutboundGuardOptions { BlockPrivateNetworks = false });
        Assert.True(policy.IsAllowed("localhost", IPAddress.Loopback));
    }

    [Fact]
    public void Default_blocks_private_and_allows_public()
    {
        var policy = new OutboundAddressPolicy(new OutboundGuardOptions());
        Assert.False(policy.IsAllowed("hook.example", IPAddress.Parse("169.254.169.254")));
        Assert.True(policy.IsAllowed("hook.example", IPAddress.Parse("8.8.8.8")));
    }

    [Theory]
    [InlineData("10.20.1.1", "x.example", true)]    // inside allowed CIDR
    [InlineData("10.21.1.1", "x.example", false)]   // outside it
    [InlineData("192.168.1.9", "x.example", true)]  // allowed single IP
    [InlineData("192.168.1.10", "x.example", false)]
    [InlineData("10.99.0.1", "hooks.corp.local", true)]   // allowed wildcard host
    [InlineData("10.99.0.1", "corp.local", false)]        // wildcard needs a subdomain
    [InlineData("10.99.0.1", "evil-corp.local", false)]
    [InlineData("10.99.0.1", "ops.internal", true)]       // allowed exact host
    public void Allow_list(string ip, string host, bool expected)
    {
        var policy = new OutboundAddressPolicy(new OutboundGuardOptions { Allow = ["10.20.0.0/16", "192.168.1.9", "*.corp.local", "ops.internal"] });
        Assert.Equal(expected, policy.IsAllowed(host, IPAddress.Parse(ip)));
    }

    [Fact]
    public async Task Guarded_client_refuses_loopback_before_any_request()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddHttpClient("g").AddOutboundGuard();
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("g");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("http://127.0.0.1:1/"));
        Assert.Contains("Blocked outbound request", ex.Message);
    }

    [Theory]
    [InlineData("http://169.254.169.254/latest", true)]
    [InlineData("https://127.0.0.1:8123/", true)]
    [InlineData("http://8.8.8.8/hook", false)]
    [InlineData("not a url", false)]
    [InlineData("ftp://10.0.0.1/", false)]
    public async Task CheckUrlAsync_reports_refusals(string url, bool refused)
    {
        var message = await new OutboundAddressPolicy(new OutboundGuardOptions()).CheckUrlAsync(url, CancellationToken.None);
        Assert.Equal(refused, message is not null);
    }

    [Fact]
    public async Task Guarded_client_refuses_metadata_address()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build());
        services.AddHttpClient("p").AddOutboundGuard();
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient("p");
        await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("http://169.254.169.254/"));
    }
}
