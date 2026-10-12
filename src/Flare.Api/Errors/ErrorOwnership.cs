using System.Text;
using System.Text.RegularExpressions;
using Flare.Api.Model;

namespace Flare.Api.Errors;

/// <summary>Who owns an error group and why: an explicit assignee, or the ownership rule that matched.</summary>
public sealed record ErrorGroupOwner(string Owner, bool FromRule);

/// <summary>
/// Server-side port of the dashboard's <c>src/dashboard/src/lib/errors/ownership.ts</c> (ADR-0195,
/// ADR-0196), so alert messages can name the owner. The two implementations must agree - when one
/// changes, change the other, and the cases in <c>ErrorOwnershipTests</c> mirror that file's.
/// Pure: no I/O.
/// </summary>
public static class ErrorOwnership
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant;

    /// <summary>Case-insensitive glob: <c>*</c> matches any run of characters (including none), everything else is literal.</summary>
    public static bool GlobMatches(string pattern, string value)
    {
        var source = string.Join(".*", pattern.Split('*').Select(Regex.Escape));
        return Regex.IsMatch(value, $@"^{source}\z", Options);
    }

    /// <summary>
    /// CODEOWNERS / gitignore-style match against a repo-relative path. A pattern with a slash (other
    /// than a trailing one) is anchored at the repo root, one without matches at any depth; a trailing
    /// <c>/</c> means a directory; <c>*</c> stays inside a segment, <c>**</c> crosses segments, <c>?</c>
    /// is one character. A pattern naming a directory also owns everything beneath it.
    /// </summary>
    public static bool CodeownersMatches(string pattern, string path)
    {
        var p = pattern.Trim().Replace('\\', '/');
        if (p.Length == 0)
        {
            return false;
        }

        if (p.EndsWith('/'))
        {
            p = p[..^1];
        }

        var anchored = p.StartsWith('/') || p.Contains('/');
        p = p.TrimStart('/');
        if (p.Length == 0)
        {
            return true; // "/" owns the whole repo
        }

        var source = new StringBuilder();
        for (var i = 0; i < p.Length; i++)
        {
            var c = p[i];
            if (c == '*')
            {
                if (i + 1 < p.Length && p[i + 1] == '*')
                {
                    i++;
                    if (i + 1 < p.Length && p[i + 1] == '/')
                    {
                        i++;
                        source.Append("(?:.*/)?"); // "**/" = zero or more directories
                    }
                    else
                    {
                        source.Append(".*");
                    }
                }
                else
                {
                    source.Append("[^/]*");
                }
            }
            else if (c == '?')
            {
                source.Append("[^/]");
            }
            else
            {
                source.Append(Regex.Escape(c.ToString()));
            }
        }

        var regex = $@"^{(anchored ? "" : "(?:.*/)?")}{source}(?:/.*)?\z";
        return Regex.IsMatch(path.Replace('\\', '/').TrimStart('/'), regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Candidate repo-relative paths for a build-machine path. The checkout prefix isn't known, so
    /// every trailing run of segments is tried (<c>/ci/build/src/pay/Charge.cs</c> -> <c>ci/build/src/...</c>,
    /// <c>build/src/...</c>, <c>src/pay/Charge.cs</c>, ...).
    /// </summary>
    public static IReadOnlyList<string> PathSuffixes(string path)
    {
        var segments = Regex.Replace(path.Replace('\\', '/'), "^[a-z]:", "", RegexOptions.IgnoreCase)
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Select((_, i) => string.Join('/', segments[i..])).ToList();
    }

    /// <summary>The rule that owns a group - the first in list order matching its service, type or sample paths - or null.</summary>
    public static ErrorOwnershipRule? OwnerFromRules(IReadOnlyList<ErrorOwnershipRule> rules, string exceptionType, IReadOnlyList<string> services, IReadOnlyList<string> paths)
    {
        foreach (var rule in rules)
        {
            var hit = rule.Kind switch
            {
                ErrorOwnershipKind.Service => services.Any(s => GlobMatches(rule.Pattern, s)),
                ErrorOwnershipKind.Path => paths.Any(p => PathSuffixes(p).Any(candidate => CodeownersMatches(rule.Pattern, candidate))),
                _ => GlobMatches(rule.Pattern, exceptionType),
            };
            if (hit)
            {
                return rule;
            }
        }

        return null;
    }

    /// <summary>An explicit assignee wins; otherwise the first matching rule; otherwise null.</summary>
    public static ErrorGroupOwner? Resolve(string? assignee, IReadOnlyList<ErrorOwnershipRule> rules, string exceptionType, IReadOnlyList<string> services, IReadOnlyList<string> paths)
    {
        if (!string.IsNullOrWhiteSpace(assignee))
        {
            return new ErrorGroupOwner(assignee, FromRule: false);
        }

        return OwnerFromRules(rules, exceptionType, services, paths) is { } rule ? new ErrorGroupOwner(rule.Owner, FromRule: true) : null;
    }
}
