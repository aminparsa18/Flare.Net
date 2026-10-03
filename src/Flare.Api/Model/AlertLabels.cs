using System.Text.RegularExpressions;

namespace Flare.Api.Model;

/// <summary>
/// Validation and matching for user-defined alert rule labels (<see cref="AlertRule.Labels"/>)
/// and a maintenance window's label matchers (<see cref="MaintenanceWindow.LabelMatchers"/>).
/// Both are plain key/value maps. See <c>docs-internal/adr/0084-alert-rule-labels.md</c>.
/// </summary>
public static partial class AlertLabels
{
    public const int MaxLabels = 20;

    public const int MaxKeyLength = 64;

    public const int MaxValueLength = 200;

    /// <summary>Label keys are Prometheus-style names plus dots/dashes (so <c>service.name</c> works) and must start with a letter or underscore.</summary>
    [GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_.\-]*$")]
    private static partial Regex KeyPattern();

    /// <summary>Returns an error message (naming <paramref name="field"/>), or null when <paramref name="labels"/> is valid. Null/empty is valid.</summary>
    public static string? Validate(IReadOnlyDictionary<string, string>? labels, string field)
    {
        if (labels is null || labels.Count == 0)
        {
            return null;
        }

        if (labels.Count > MaxLabels)
        {
            return $"{field} may have at most {MaxLabels} entries.";
        }

        foreach (var (key, value) in labels)
        {
            if (key.Length > MaxKeyLength || !KeyPattern().IsMatch(key))
            {
                return $"{field} key '{key}' is invalid: use letters, digits, '_', '.' or '-' (starting with a letter or '_'), at most {MaxKeyLength} characters.";
            }

            if (string.IsNullOrWhiteSpace(value) || value.Length > MaxValueLength)
            {
                return $"{field} value for '{key}' must be 1-{MaxValueLength} characters.";
            }
        }

        return null;
    }

    /// <summary>Trims keys/values, so a stored label never differs from what a matcher compares against by stray whitespace. Null becomes an empty map.</summary>
    public static IReadOnlyDictionary<string, string> Normalize(IReadOnlyDictionary<string, string>? labels) =>
        labels is null || labels.Count == 0
            ? new Dictionary<string, string>()
            : labels.ToDictionary(kv => kv.Key.Trim(), kv => kv.Value.Trim(), StringComparer.Ordinal);

    /// <summary>True when <paramref name="labels"/> contains every key/value pair of <paramref name="matchers"/> (exact, case-sensitive). An empty <paramref name="matchers"/> matches nothing - callers treat "no matcher" separately.</summary>
    public static bool MatchesAll(IReadOnlyDictionary<string, string> matchers, IReadOnlyDictionary<string, string> labels)
    {
        if (matchers.Count == 0)
        {
            return false;
        }

        foreach (var (key, value) in matchers)
        {
            if (!labels.TryGetValue(key, out var actual) || !string.Equals(actual, value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }
}
