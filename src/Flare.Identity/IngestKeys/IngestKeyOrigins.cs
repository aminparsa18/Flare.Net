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

            // Strict on write: the caller is interactive (API/CLI), so a path is reported rather than silently dropped.
            if (!BrowserOrigins.TryNormalize(origin, allowPath: false, out var normalized))
            {
                return (null, $"'{origin}' is not a valid origin; use scheme://host[:port] with no path, e.g. https://app.example.com.");
            }

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

    /// <summary>Lenient re-normalization of stored origins (e.g. a row edited directly in the database): paths are
    /// dropped, unparseable entries skipped. Values written through <see cref="Normalize"/> pass through unchanged.</summary>
    public static IReadOnlyList<string> NormalizeStored(IEnumerable<string> stored)
    {
        var result = new List<string>();
        foreach (var value in stored)
        {
            if (BrowserOrigins.TryNormalize(value, allowPath: true, out var origin) && !result.Contains(origin))
            {
                result.Add(origin);
            }
        }

        return result;
    }
}
