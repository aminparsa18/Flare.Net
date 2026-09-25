using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
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
/// services/severities/traceId, exact-or-prefix scope names, case-insensitive substring search against
/// <see cref="LogEventDto.Body"/>, per-bag attribute equals/not-equals/exists/absent/
/// regex/not-regex/in/not-in, and the same operator vocabulary again for
/// <see cref="LogFilter.BodyJsonFilters"/> against a value parsed out of <c>Body</c>'s own
/// JSON - regex via <see cref="Regex.IsMatch(string, string)"/> rather
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

        if (filter.ScopeNames is { Count: > 0 } scopeNames && !ScopeNameMatches(logEvent.ScopeName, scopeNames))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(filter.TraceId) &&!string.Equals(logEvent.TraceId, filter.TraceId, StringComparison.Ordinal))
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

        if (filter.BodyJsonFilters is { Count: > 0 } bodyJsonFilters)
        {
            foreach (var bodyJsonFilter in bodyJsonFilters)
            {
                if (bodyJsonFilter.Operator is BodyJsonFilterOperator.Has or BodyJsonFilterOperator.NotHas)
                {
                    var has = BodyJsonArrayHas(logEvent.Body, bodyJsonFilter.Path, bodyJsonFilter.Value);
                    if (has != (bodyJsonFilter.Operator == BodyJsonFilterOperator.Has))
                    {
                        return false;
                    }

                    continue;
                }

                var exists = TryExtractBodyJsonValue(logEvent.Body, bodyJsonFilter.Path, out var value);
                var matches = bodyJsonFilter.Operator switch
                {
                    BodyJsonFilterOperator.Exists => exists,
                    BodyJsonFilterOperator.Absent => !exists,
                    BodyJsonFilterOperator.NotEquals => !(exists && string.Equals(value, bodyJsonFilter.Value, StringComparison.Ordinal)),
                    BodyJsonFilterOperator.Regex => exists && RegexMatches(value!, bodyJsonFilter.Value),
                    BodyJsonFilterOperator.NotRegex => !(exists && RegexMatches(value!, bodyJsonFilter.Value)),
                    BodyJsonFilterOperator.In => exists && InValues(value!, bodyJsonFilter.Values),
                    BodyJsonFilterOperator.NotIn => !(exists && InValues(value!, bodyJsonFilter.Values)),
                    _ => exists && string.Equals(value, bodyJsonFilter.Value, StringComparison.Ordinal),
                };
                if (!matches)
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Mirrors <see cref="LogFilterSqlBuilder"/>'s <c>ScopeNamesClause</c>: exact (ordinal) match on any plain entry, or an ordinal prefix match on any <c>*</c>-suffixed one.</summary>
    private static bool ScopeNameMatches(string scopeName, IReadOnlyList<string> scopeNames)
    {
        var (exact, prefixes) = LogFilterSqlBuilder.SplitScopeNames(scopeNames);
        return exact.Contains(scopeName, StringComparer.Ordinal)
            || prefixes.Exists(prefix => scopeName.StartsWith(prefix, StringComparison.Ordinal));
    }

    /// <summary>
    /// <see cref="BodyJsonFilter.Path"/> resolved against <paramref name="body"/> -
    /// mirrors <see cref="LogFilterSqlBuilder"/>'s <c>BodyJsonClause</c> (dot-separated
    /// object-key segments, no array indices). Fail-closed on non-JSON/malformed
    /// <c>Body</c> (returns <c>false</c>, same posture <see cref="RegexMatches"/> takes for
    /// an invalid pattern) rather than letting <see cref="JsonException"/> escape into
    /// <see cref="Flare.Api.LiveTail.LogTailBroadcaster"/>'s shared matching loop. A found
    /// path always returns <c>true</c> regardless of its value's kind - including a JSON
    /// <c>null</c> leaf, matching ClickHouse's <c>JSONHas</c> which also counts a
    /// present-but-null key as "has". Non-string leaves (number/bool/object/array) render
    /// via <see cref="JsonElement.GetRawText"/>, matching <c>JSONExtractString</c>'s own
    /// observed (not just documented) behavior of stringifying non-string JSON values
    /// rather than returning empty for them.
    /// </summary>
    private static bool TryExtractBodyJsonValue(string body, string path, out string? value)
    {
        value = null;
        if (!TryParse(body, out var document))
        {
            return false;
        }

        using (document)
        {
            if (!TryResolvePath(document.RootElement, path, out var element))
            {
                return false;
            }

            value = Stringify(element);
            return true;
        }
    }

    /// <summary>
    /// <see cref="BodyJsonFilterOperator.Has"/>'s test - mirrors <see cref="LogFilterSqlBuilder"/>'s
    /// <c>has(JSONExtract(Body, ..., 'Array(String)'), value)</c>: each element is
    /// stringified the same way <see cref="TryExtractBodyJsonValue"/> renders a scalar
    /// leaf, and a missing path, a non-array value, or non-JSON <c>Body</c> is simply
    /// <c>false</c> (ClickHouse extracts <c>[]</c> for all three).
    /// </summary>
    private static bool BodyJsonArrayHas(string body, string path, string value)
    {
        if (!TryParse(body, out var document))
        {
            return false;
        }

        using (document)
        {
            if (!TryResolvePath(document.RootElement, path, out var element) || element.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var item in element.EnumerateArray())
            {
                if (string.Equals(Stringify(item), value, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static bool TryParse(string body, [NotNullWhen(true)] out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(body);
            return true;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }

    private static bool TryResolvePath(JsonElement root, string path, out JsonElement element)
    {
        element = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element))
            {
                return false;
            }
        }

        return true;
    }

    private static string Stringify(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString()!,
        JsonValueKind.Null => "",
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => element.GetRawText(),
    };

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
