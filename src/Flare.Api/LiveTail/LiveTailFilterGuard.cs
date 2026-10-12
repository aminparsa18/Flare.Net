using System.Text.RegularExpressions;
using Flare.Api.Model;

namespace Flare.Api.LiveTail;

/// <summary>
/// Validates a live-tail subscription's <see cref="LogFilter"/> against the <see cref="LiveTailOptions"/>
/// size limits and compiles its regex operators once. The broadcaster evaluates every subscription's
/// filter inside one shared loop, so an unbounded filter or a catastrophic-backtracking pattern from
/// one Viewer would stall live tail for everyone.
/// </summary>
public static class LiveTailFilterGuard
{
    /// <summary>
    /// Returns the compiled regexes keyed by pattern text, or null with <paramref name="error"/> set
    /// when the filter exceeds a limit or holds an invalid pattern. Patterns use
    /// <see cref="RegexOptions.NonBacktracking"/> (linear time; no lookarounds or backreferences) and a match timeout.
    /// </summary>
    public static IReadOnlyDictionary<string, Regex>? TryCompile(LogFilter filter, LiveTailOptions options, out string? error)
    {
        error = null;

        var items = (filter.Services?.Count ?? 0) + (filter.SeverityNumbers?.Count ?? 0) + (filter.ScopeNames?.Count ?? 0)
            + (filter.Attributes?.Count ?? 0) + (filter.BodyJsonFilters?.Count ?? 0);
        var longString = Longest(filter.Search, filter.TraceId, filter.SpanId, filter.PatternId);
        var patterns = new List<string>();

        foreach (var service in filter.Services ?? [])
        {
            longString = Math.Max(longString, service.Length);
        }

        foreach (var scope in filter.ScopeNames ?? [])
        {
            longString = Math.Max(longString, scope.Length);
        }

        foreach (var attribute in filter.Attributes ?? [])
        {
            items += attribute.Values?.Count ?? 0;
            longString = Math.Max(longString, Longest(attribute.Key, attribute.Value, attribute.Values));
            if (attribute.Operator is AttributeFilterOperator.Regex or AttributeFilterOperator.NotRegex)
            {
                patterns.Add(attribute.Value);
            }
        }

        foreach (var bodyFilter in filter.BodyJsonFilters ?? [])
        {
            items += bodyFilter.Values?.Count ?? 0;
            longString = Math.Max(longString, Longest(bodyFilter.Path, bodyFilter.Value, bodyFilter.Values));
            if (bodyFilter.Operator is BodyJsonFilterOperator.Regex or BodyJsonFilterOperator.NotRegex)
            {
                patterns.Add(bodyFilter.Value);
            }
        }

        if (items > options.MaxFilterItems)
        {
            error = $"Filter is too large: {items} entries, the limit is {options.MaxFilterItems}.";
            return null;
        }

        if (longString > options.MaxFilterStringLength)
        {
            error = $"Filter contains a value longer than {options.MaxFilterStringLength} characters.";
            return null;
        }

        var distinct = patterns.Distinct(StringComparer.Ordinal).ToList();
        if (distinct.Count > options.MaxRegexPatterns)
        {
            error = $"Filter has {distinct.Count} regex patterns, the limit is {options.MaxRegexPatterns}.";
            return null;
        }

        var compiled = new Dictionary<string, Regex>(StringComparer.Ordinal);
        foreach (var pattern in distinct)
        {
            if (pattern.Length > options.MaxRegexPatternLength)
            {
                error = $"Regex pattern is longer than {options.MaxRegexPatternLength} characters.";
                return null;
            }

            try
            {
                compiled[pattern] = new Regex(pattern, RegexOptions.NonBacktracking, options.RegexMatchTimeout);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                error = $"Invalid or unsupported regex pattern '{Truncate(pattern)}': live tail supports linear-time patterns only (no lookarounds or backreferences).";
                return null;
            }
        }

        return compiled;
    }

    private static int Longest(params string?[] values) => values.Max(v => v?.Length ?? 0);

    private static int Longest(string? a, string? b, IReadOnlyList<string>? many) =>
        Math.Max(Math.Max(a?.Length ?? 0, b?.Length ?? 0), many is null ? 0 : many.Select(v => v.Length).DefaultIfEmpty(0).Max());

    private static string Truncate(string s) => s.Length <= 40 ? s : s[..40] + "...";
}
