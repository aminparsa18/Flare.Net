namespace Flare.Identity.Projects;

/// <summary>
/// The pattern language for a project's service allow-list (ADR-0123): an exact
/// <c>service.name</c>, or a trailing <c>*</c> for a prefix (<c>checkout-*</c>). Same
/// convention as <c>LogFilter.ScopeNames</c>. Case-sensitive, like ClickHouse string equality.
/// </summary>
public static class ProjectServicePattern
{
    public const int MaxLength = 200;

    /// <summary>Null when valid, otherwise why not.</summary>
    public static string? Validate(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return "Pattern must not be empty.";
        }

        if (pattern != pattern.Trim() || pattern.Length > MaxLength)
        {
            return $"Pattern must be trimmed and at most {MaxLength} characters.";
        }

        var star = pattern.IndexOf('*');
        if (star >= 0 && star != pattern.Length - 1)
        {
            return "'*' is only allowed as the last character.";
        }

        return pattern == "*" ? "A bare '*' would match every service; use a prefix such as 'app-*'." : null;
    }

    public static bool Matches(string pattern, string serviceName) =>
        pattern.EndsWith('*')
            ? serviceName.StartsWith(pattern[..^1], StringComparison.Ordinal)
            : string.Equals(pattern, serviceName, StringComparison.Ordinal);

    public static bool MatchesAny(IEnumerable<string> patterns, string serviceName) =>
        patterns.Any(p => Matches(p, serviceName));
}
