namespace Flare.Identity;

/// <summary>
/// Turns configured or user-entered values into browser origins: <c>scheme://host[:port]</c>, the exact form a browser
/// sends in its <c>Origin</c> header. Scheme and host are lowercased, IDN hosts become punycode, default ports and
/// userinfo are dropped. Shared by Flare.Api (<c>Cors:AllowedOrigins</c>), Flare.Ingest (<c>Otlp:AllowedOrigins</c>)
/// and per-ingest-key origins (<see cref="IngestKeys.IngestKeyOrigins"/>), so a value with a trailing slash or a path
/// still matches what the browser sends.
/// </summary>
public static class BrowserOrigins
{
    /// <summary>The wildcard entry; only meaningful where a caller opts in.</summary>
    public const string Wildcard = "*";

    /// <summary>
    /// Normalizes one value. With <paramref name="allowPath"/>, a path, query or fragment (including a lone trailing
    /// slash) is discarded; without it, anything past the authority other than a single trailing slash is rejected.
    /// False for anything that is not an absolute http/https URL with a host.
    /// </summary>
    public static bool TryNormalize(string? raw, bool allowPath, out string origin)
    {
        origin = "";
        var value = raw?.Trim();
        if (string.IsNullOrEmpty(value)
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(uri.Host))
        {
            return false;
        }

        if (!allowPath
            && (uri.AbsolutePath != "/"
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)
                || value.EndsWith("//", StringComparison.Ordinal)))
        {
            return false;
        }

        // Built by hand rather than GetLeftPart(UriPartial.Authority), which keeps any userinfo (https://user@host).
        // IdnHost is punycode (what browsers send) and keeps IPv6 brackets.
        var host = uri.IdnHost.ToLowerInvariant();
        if (uri.HostNameType == UriHostNameType.IPv6 && !host.StartsWith('['))
        {
            host = $"[{host}]";
        }

        origin = uri.IsDefaultPort ? $"{uri.Scheme}://{host}" : $"{uri.Scheme}://{host}:{uri.Port}";
        return true;
    }

    /// <summary>
    /// Normalizes a configured allow-list (e.g. <c>Cors:AllowedOrigins</c>): blank entries are skipped, paths and
    /// trailing slashes dropped, duplicates removed. Throws <see cref="InvalidOperationException"/> naming
    /// <c>{configKey}:{index}</c> for an entry that is not an absolute http/https URL, or for <c>*</c> unless
    /// <paramref name="allowWildcard"/>, so a typo fails at startup instead of silently blocking every browser call.
    /// </summary>
    public static IReadOnlyList<string> NormalizeConfigured(IEnumerable<string?>? values, string configKey, bool allowWildcard)
    {
        var result = new List<string>();
        var index = -1;
        foreach (var raw in values ?? [])
        {
            index++;
            var value = raw?.Trim();
            if (string.IsNullOrEmpty(value))
            {
                continue;
            }

            string origin;
            if (value == Wildcard)
            {
                if (!allowWildcard)
                {
                    throw new InvalidOperationException(
                        $"{configKey}:{index} is '*', which is not supported here: list the exact origin(s), e.g. https://flare.example.com.");
                }

                origin = Wildcard;
            }
            else if (!TryNormalize(value, allowPath: true, out origin))
            {
                throw new InvalidOperationException(
                    $"{configKey}:{index} '{value}' is not an absolute http(s) URL; use an origin such as https://flare.example.com.");
            }

            if (!result.Contains(origin))
            {
                result.Add(origin);
            }
        }

        return result;
    }
}
