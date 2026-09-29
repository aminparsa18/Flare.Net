using System.Diagnostics;
using OpenTelemetry;

namespace ExampleApp.Shop;

/// <summary>
/// Sets <c>peer.service</c> on outgoing HTTP client spans that target another shop service.
/// </summary>
/// <remarks>
/// Service discovery rewrites <c>http://checkout-api/...</c> to <c>http://localhost:&lt;port&gt;</c>
/// before the HttpClient span starts, so without this every service-to-service call would
/// read as a call to an external host named <c>localhost</c> - Flare's External APIs page
/// counts any <c>CLIENT</c> span with a <c>server.address</c> and no <c>db.system</c>/
/// <c>messaging.system</c>/<c>peer.service</c> (ADR-0071). <c>peer.service</c> is the OTel
/// attribute for exactly this ("the logical name of the remote service"), and is what the
/// Services page's dependency breakdown groups by. The address-to-name map is built from the
/// same <c>services__&lt;name&gt;__&lt;endpoint&gt;__&lt;n&gt;</c> config service discovery
/// itself reads.
/// </remarks>
public sealed class PeerServiceProcessor : BaseProcessor<Activity>
{
    private readonly Dictionary<string, string> _servicesByAuthority = new(StringComparer.OrdinalIgnoreCase);

    public PeerServiceProcessor(IConfiguration configuration)
    {
        foreach (var service in configuration.GetSection("services").GetChildren())
        {
            foreach (var address in service.GetChildren().SelectMany(endpoint => endpoint.GetChildren()))
            {
                if (Uri.TryCreate(address.Value, UriKind.Absolute, out var uri))
                {
                    _servicesByAuthority[$"{uri.Host}:{uri.Port}"] = service.Key;
                }
            }
        }
    }

    public override void OnEnd(Activity activity)
    {
        if (activity.Kind != ActivityKind.Client || _servicesByAuthority.Count == 0 || activity.GetTagItem("peer.service") is not null)
        {
            return;
        }

        if (activity.GetTagItem("server.address") is string host
            && activity.GetTagItem("server.port") is { } port
            && _servicesByAuthority.TryGetValue($"{host}:{port}", out var service))
        {
            activity.SetTag("peer.service", service);
        }
    }
}
