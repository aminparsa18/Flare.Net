using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Flare.ServiceDefaults.Outbound;

public static class OutboundGuardExtensions
{
    /// <summary>
    /// Makes this client refuse connections to non-public addresses (<see cref="OutboundGuardOptions"/>). The check runs
    /// in the connect callback, on the address that is then actually connected to, so a redirect, a DNS name that
    /// resolves to a private address, or a rebinding resolver cannot get past it.
    /// </summary>
    public static IHttpClientBuilder AddOutboundGuard(this IHttpClientBuilder builder)
    {
        builder.Services.AddOptions<OutboundGuardOptions>().BindConfiguration(OutboundGuardOptions.SectionName);
        // The connect callback sees only the proxy when one is configured, so the target is also checked up front.
        builder.AddHttpMessageHandler(sp => new ProxyTargetCheckHandler(new OutboundAddressPolicy(sp.GetRequiredService<IOptions<OutboundGuardOptions>>().Value)));
        return builder.ConfigurePrimaryHttpMessageHandler(sp =>
        {
            var policy = new OutboundAddressPolicy(sp.GetRequiredService<IOptions<OutboundGuardOptions>>().Value);
            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) };
            if (policy.Enabled)
            {
                handler.ConnectCallback = (context, ct) => ConnectAsync(policy, context, ct);
            }

            return handler;
        });
    }

    private static async ValueTask<Stream> ConnectAsync(OutboundAddressPolicy policy, SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var host = context.DnsEndPoint.Host;
        var addresses = await policy.ResolveCheckedAsync(host, ct);

        Exception? last = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException)
            {
                socket.Dispose();
                last = ex;
            }
        }

        throw new HttpRequestException($"Could not connect to {host}: {last?.Message ?? "no addresses resolved"}", last);
    }
}

/// <summary>Best-effort target check for requests that go through a proxy (the proxy resolves the name itself, so rebinding is still its egress rules' job).</summary>
internal sealed class ProxyTargetCheckHandler(OutboundAddressPolicy policy) : DelegatingHandler
{
    private readonly IWebProxy _proxy = HttpClient.DefaultProxy;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (policy.Enabled && request.RequestUri is { } uri && !_proxy.IsBypassed(uri))
        {
            await policy.ResolveCheckedAsync(uri.DnsSafeHost, cancellationToken);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
