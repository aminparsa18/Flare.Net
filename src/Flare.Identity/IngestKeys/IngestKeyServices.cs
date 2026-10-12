namespace Flare.Identity.IngestKeys;

/// <summary>Normalizes and (de)serializes an ingest key's service allowlist (ADR-0150).
/// Names are <c>service.name</c> values, compared case-sensitively like the rest of Flare.</summary>
public static class IngestKeyServices
{
    private const char Separator = '\n';

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? services) =>
        (services ?? []).Select(s => s?.Trim() ?? string.Empty).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).ToList();

    public static string? Serialize(IReadOnlyList<string> services) =>
        services.Count == 0 ? null : string.Join(Separator, services);

    public static IReadOnlyList<string> Deserialize(string? stored) =>
        string.IsNullOrEmpty(stored) ? [] : stored.Split(Separator, StringSplitOptions.RemoveEmptyEntries);
}
