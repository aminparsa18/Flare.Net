namespace Flare.Api.Model;

/// <summary>
/// Validation and normalization for a dashboard's free-form tags (<see cref="Dashboard.Tags"/>).
/// See <c>docs-internal/adr/0089-dashboard-tags-and-pins.md</c>.
/// </summary>
public static class DashboardTags
{
    public const int MaxTags = 10;

    public const int MaxTagLength = 32;

    /// <summary>Returns an error message, or null when <paramref name="tags"/> is valid. Null/empty is valid.</summary>
    public static string? Validate(IReadOnlyList<string>? tags)
    {
        if (tags is null)
        {
            return null;
        }

        var normalized = Normalize(tags);
        if (normalized.Count > MaxTags)
        {
            return $"A dashboard may have at most {MaxTags} tags.";
        }

        return tags.Any(t => t.Trim().Length > MaxTagLength) ? $"A tag may be at most {MaxTagLength} characters." : null;
    }

    /// <summary>Trims, lowercases (so "Prod" and "prod" are one tag), drops blanks and duplicates, and keeps first-seen order.</summary>
    public static IReadOnlyList<string> Normalize(IReadOnlyList<string>? tags) =>
        tags is null
            ? []
            : tags.Select(t => t.Trim().ToLowerInvariant())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
}
