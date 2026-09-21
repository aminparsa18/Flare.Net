using System.Text.RegularExpressions;
using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline.Rules;

/// <summary>
/// Pure <see cref="PipelineRuleCondition"/> → boolean match against a <see cref="LogEvent"/> -
/// the ingest-time counterpart to <c>Flare.Api.Query.LogFilterMatcher</c>'s live-tail
/// matcher, mirroring its semantics field-for-field for the subset of conditions
/// <see cref="PipelineRuleCondition"/> carries (see its remarks for what's deliberately
/// excluded and why). No I/O dependency, same "pure function, unit-testable on its own"
/// style.
/// </summary>
/// <remarks>
/// An empty <see cref="PipelineRuleCondition"/> (every field null/empty) matches every
/// event - deliberate, not a bug: the dashboard form surfaces that as an explicit "matches
/// all logs" notice rather than a silent default (see
/// docs-internal/adr/0033-pipeline-rules-extraction-redaction.md).
/// </remarks>
public static class PipelineRuleConditionMatcher
{
    public static bool Matches(LogEvent logEvent, PipelineRuleCondition condition)
    {
        if (condition.Services is { Count: > 0 } services && !services.Contains(logEvent.ServiceName, StringComparer.Ordinal))
        {
            return false;
        }

        if (condition.SeverityNumbers is { Count: > 0 } severities && !severities.Contains((byte)logEvent.SeverityNumber))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(condition.Search)
            && !(logEvent.Body ?? "").Contains(condition.Search, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (condition.Attributes is { Count: > 0 } attributes)
        {
            foreach (var attribute in attributes)
            {
                var bag = BagFor(logEvent, attribute.Bag);
                var exists = bag.TryGetValue(attribute.Key, out var value);
                var matches = attribute.Operator switch
                {
                    AttributeConditionOperator.Exists => exists,
                    AttributeConditionOperator.Absent => !exists,
                    AttributeConditionOperator.NotEquals => !(exists && string.Equals(value, attribute.Value, StringComparison.Ordinal)),
                    // value! - RegexMatches only runs once exists is true (short-circuited by &&),
                    // at which point TryGetValue guarantees value is non-null; the compiler can't
                    // see that guarantee here since exists was captured into its own variable.
                    AttributeConditionOperator.Regex => exists && RegexMatches(value!, attribute.Value),
                    AttributeConditionOperator.NotRegex => !(exists && RegexMatches(value!, attribute.Value)),
                    AttributeConditionOperator.In => exists && InValues(value!, attribute.Values),
                    AttributeConditionOperator.NotIn => !(exists && InValues(value!, attribute.Values)),
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

    /// <summary>Fail-closed on an invalid pattern (returns <see langword="false"/>) - same posture as <c>LogFilterMatcher.RegexMatches</c>: a rule with a bad attribute-condition pattern shouldn't take down annotation for every other event in the batch.</summary>
    private static bool RegexMatches(string value, string pattern)
    {
        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool InValues(string value, IReadOnlyList<string>? values) =>
        values is { Count: > 0 } && values.Contains(value, StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, string> BagFor(LogEvent logEvent, AttributeBag bag) => bag switch
    {
        AttributeBag.Resource => logEvent.ResourceAttributes,
        AttributeBag.Scope => logEvent.ScopeAttributes,
        _ => logEvent.LogAttributes,
    };
}
