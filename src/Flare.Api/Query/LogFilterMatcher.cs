using System.Text.RegularExpressions;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Pure <see cref="LogFilter"/> → boolean match, the live-tail endpoint's in-memory
/// counterpart to <see cref="LogFilterSqlBuilder"/>'s SQL translation. Deliberately has no
/// I/O dependency, same "pure function, unit-testable on its own" style as the other query
/// builders in this namespace.
/// </summary>
/// <remarks>
/// Mirrors <see cref="LogFilterSqlBuilder"/>'s semantics field-for-field (exact-match
/// services/severities/traceId, case-insensitive substring search against
/// <see cref="LogEventDto.Body"/>, per-bag attribute equals/not-equals/exists/absent/
/// regex/not-regex/in/not-in - regex via <see cref="Regex.IsMatch(string, string)"/> rather
/// than ClickHouse's RE2-based <c>match()</c>, close enough for live-tail's purposes even
/// though the two regex engines aren't byte-for-byte identical) with one deliberate
/// exception: <see cref="LogFilter.From"/>/<see cref="LogFilter.To"/> are ignored - a live
/// tail is inherently an open-ended stream of events observed from the moment of
/// subscription onward, not a bounded historical range; use <c>/api/logs/search</c> for
/// that.
/// </remarks>
public static class LogFilterMatcher
{
    public static bool Matches(LogEventDto logEvent, LogFilter filter)
    {
        if (filter.Services is { Count: > 0 } services && !services.Contains(logEvent.ServiceName, StringComparer.Ordinal))
        {
            return false;
        }

        if (filter.SeverityNumbers is { Count: > 0 } severities && !severities.Contains(logEvent.SeverityNumber))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filter.TraceId) && !string.Equals(logEvent.TraceId, filter.TraceId, StringComparison.Ordinal))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filter.Search)
            && !logEvent.Body.Contains(filter.Search, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (filter.Attributes is { Count: > 0 } attributes)
        {
            foreach (var attribute in attributes)
            {
                var bag = BagFor(logEvent, attribute.Bag);
                var exists = bag.TryGetValue(attribute.Key, out var value);
                var matches = attribute.Operator switch
                {
                    AttributeFilterOperator.Exists => exists,
                    AttributeFilterOperator.Absent => !exists,
                    AttributeFilterOperator.NotEquals => !(exists && string.Equals(value, attribute.Value, StringComparison.Ordinal)),
                    // value! - RegexMatches only runs once exists is true (short-circuited by &&),
                    // at which point TryGetValue guarantees value is non-null; the compiler can't
                    // see that guarantee here since exists was captured into its own variable.
                    AttributeFilterOperator.Regex => exists && RegexMatches(value!, attribute.Value),
                    AttributeFilterOperator.NotRegex => !(exists && RegexMatches(value!, attribute.Value)),
                    AttributeFilterOperator.In => exists && InValues(value!, attribute.Values),
                    AttributeFilterOperator.NotIn => !(exists && InValues(value!, attribute.Values)),
                    _ => exists && string.Equals(value, attribute.Value, StringComparison.Ordinal),
                };
                if (!matches)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// <see cref="Regex.IsMatch(string, string)"/>, fail-closed on an invalid pattern
    /// (returns <c>false</c>) rather than letting <see cref="RegexParseException"/> escape.
    /// Unlike a malformed pattern on the SQL side - where ClickHouse's <c>match()</c>
    /// simply fails that one request - <see cref="Flare.Api.LiveTail.LogTailBroadcaster"/>
    /// calls <see cref="Matches"/> in a single loop shared by every live-tail subscription, so an
    /// uncaught exception here would take down live tail for every other subscriber too,
    /// not just the one whose filter has the bad pattern.
    /// </summary>
    private static bool RegexMatches(string value, string pattern)
    {
        try
        {
            return Regex.IsMatch(value, pattern);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// <see cref="AttributeFilterOperator.In"/>/<see cref="AttributeFilterOperator.NotIn"/>'s
    /// membership test - an ordinal-comparison <see cref="Enumerable.Contains{TSource}(IEnumerable{TSource},TSource,IEqualityComparer{TSource}?)"/>
    /// against <paramref name="values"/>, same as <see cref="LogFilterSqlBuilder"/>'s
    /// <c>IN</c> clause. A null/empty <paramref name="values"/> never matches, mirroring an
    /// empty ClickHouse <c>Array(String)</c> in an <c>IN</c> list.
    /// </summary>
    private static bool InValues(string value, IReadOnlyList<string>? values) =>
        values is { Count: > 0 } && values.Contains(value, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> BagFor(LogEventDto logEvent, AttributeBag bag) => bag switch
    {
        AttributeBag.Resource => logEvent.ResourceAttributes,
        AttributeBag.Scope => logEvent.ScopeAttributes,
        _ => logEvent.LogAttributes,
    };
}
