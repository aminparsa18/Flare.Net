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
/// <see cref="LogEventDto.Body"/>, per-bag attribute equality) with one deliberate
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
                if (!bag.TryGetValue(attribute.Key, out var value) || !string.Equals(value, attribute.Value, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static IReadOnlyDictionary<string, string> BagFor(LogEventDto logEvent, AttributeBag bag) => bag switch
    {
        AttributeBag.Resource => logEvent.ResourceAttributes,
        AttributeBag.Scope => logEvent.ScopeAttributes,
        _ => logEvent.LogAttributes,
    };
}
