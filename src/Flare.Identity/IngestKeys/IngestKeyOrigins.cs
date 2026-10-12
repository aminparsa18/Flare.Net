namespace Flare.Identity.IngestKeys;

/// <summary>Normalizes and (de)serializes an ingest key's browser-origin allowlist (ADR-0149).
/// An origin is <c>scheme://host[:port]</c> with no path, compared case-insensitively.</summary>
public static class IngestKeyOrigins
{
    private const char Separator = '\n';

    /// <summary>Returns the normalized, de-duplicated origins, or an error message for the first invalid entry.</summary>
    public static (IReadOnlyList<string>? Origins, string? Error) Normalize(IEnumerable<string>? origins)
    {
        var result = new List<string>();
        foreach (var raw in origins ?? [])
        {
            var origin = raw?.Trim();
            if (string.IsNullOrEmpty(origin))
            {
                continue;
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || uri.AbsolutePath != "/"
                || !string.IsNullOrEmpty(uri.Query)
                || !string.IsNullOrEmpty(uri.Fragment)
                || origin.EndsWith("//", StringComparison.Ordinal))
            {
                return (null, $"'{origin}' is not a valid origin; use scheme://host[:port] with no path, e.g. https://app.example.com.");
            }

            var normalized = uri.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
            if (!result.Contains(normalized))
            {
                result.Add(normalized);
            }
        }

        return (result, null);
    }

    public static string? Serialize(IReadOnlyList<string> origins) =>
        origins.Count == 0 ? null : string.Join(Separator, origins);

    public static IReadOnlyList<string> Deserialize(string? stored) =>
        string.IsNullOrEmpty(stored) ? [] : stored.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
}
