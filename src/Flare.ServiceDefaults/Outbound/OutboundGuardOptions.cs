namespace Flare.ServiceDefaults.Outbound;

/// <summary>
/// <c>Outbound:*</c> - the SSRF guard (ADR-0201) on HTTP clients that call a URL a user typed in
/// (alert channels, OTLP forwarding targets). Set it the same way on every process that sends such requests.
/// </summary>
public sealed class OutboundGuardOptions
{
    public const string SectionName = "Outbound";

    /// <summary>Refuse connections to loopback, link-local (cloud metadata), private (RFC 1918 / ULA), CGNAT and other non-public addresses. Default on.</summary>
    public bool BlockPrivateNetworks { get; set; } = true;

    /// <summary>
    /// Destinations exempt from the block: an IP (<c>10.1.2.3</c>), a CIDR (<c>10.20.0.0/16</c>), or a host name,
    /// exact (<c>hooks.corp.local</c>) or a suffix wildcard (<c>*.corp.local</c>). A host name is matched against the name
    /// in the URL; the address it resolves to is still the one connected to.
    /// </summary>
    public string[] Allow { get; set; } = [];
}
