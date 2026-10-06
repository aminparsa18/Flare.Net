namespace Flare.Api.Synthetic;

/// <summary>Parsing for the <c>host:port</c> targets of Tcp and Tls monitors - pure.</summary>
public static class SyntheticTarget
{
    /// <summary>
    /// Splits <paramref name="target"/> into host and port. A bracketed IPv6 literal (<c>[::1]:443</c>) is
    /// supported. Returns null when the host is empty or the port is missing (and there is no
    /// <paramref name="defaultPort"/>) or out of range.
    /// </summary>
    public static (string Host, int Port)? ParseHostPort(string target, int? defaultPort)
    {
        var text = target.Trim();
        string host;
        string? portText;
        if (text.StartsWith('['))
        {
            var close = text.IndexOf(']');
            if (close < 0)
            {
                return null;
            }

            host = text[1..close];
            var rest = text[(close + 1)..];
            portText = rest.StartsWith(':') ? rest[1..] : rest.Length == 0 ? null : "x";
        }
        else
        {
            var colon = text.LastIndexOf(':');
            if (colon >= 0 && text.IndexOf(':') != colon)
            {
                return null; // an unbracketed IPv6 address is ambiguous about where the port starts
            }

            host = colon < 0 ? text : text[..colon];
            portText = colon < 0 ? null : text[(colon + 1)..];
        }

        if (host.Length == 0 || host.Any(char.IsWhiteSpace) || host.Contains('/'))
        {
            return null;
        }

        if (portText is null)
        {
            return defaultPort is { } p ? (host, p) : null;
        }

        return int.TryParse(portText, out var port) && port is >= 1 and <= 65535 ? (host, port) : null;
    }
}
