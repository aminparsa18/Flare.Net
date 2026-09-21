using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline.Rules;

/// <summary>
/// Pure <see cref="LogEvent"/> → <see cref="LogEvent"/> rule application - for every rule
/// (in list order, i.e. ascending <c>CreatedAt</c> per <see cref="ClickHousePipelineRuleStore"/>'s
/// query) whose <see cref="PipelineRule.Condition"/> matches (via
/// <see cref="PipelineRuleConditionMatcher"/>), applies its <see cref="PipelineRule.Actions"/>
/// in order, each action seeing the previous one's output - same "mutate the whole batch
/// via immutable <c>with</c> copies" style as <c>Patterns.LogPatternAnnotator</c>.
/// </summary>
/// <remarks>
/// A pattern that fails to compile or exceeds its match timeout is skipped (the action is
/// a no-op for that event) rather than throwing - same fail-closed posture
/// <c>LogFilterMatcher.RegexMatches</c> documents, and doubly redundant here since
/// <c>Flare.Api.Model.PipelineRuleRequest.Validate</c> already rejects an uncompilable
/// pattern at rule creation/update time; this only matters for the rare case a pattern
/// that compiled fine hits its timeout against a specific event's input. Compiled
/// <see cref="Regex"/> instances are cached per pattern string in a process-wide
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> so a hot rule isn't recompiling its
/// pattern on every event in every flush batch.
/// </remarks>
public static class PipelineRuleExecutor
{
    private static readonly ConcurrentDictionary<string, Regex?> CompiledPatterns = new();

    public static LogEvent Apply(LogEvent logEvent, IReadOnlyList<PipelineRule> rules)
    {
        foreach (var rule in rules)
        {
            if (!PipelineRuleConditionMatcher.Matches(logEvent, rule.Condition))
            {
                continue;
            }

            foreach (var action in rule.Actions)
            {
                logEvent = ApplyAction(logEvent, action);
            }
        }

        return logEvent;
    }

    private static LogEvent ApplyAction(LogEvent logEvent, PipelineRuleAction action) => action.Kind switch
    {
        RuleActionKind.ExtractRegex when action.ExtractRegex is { } extract => ApplyExtract(logEvent, extract),
        RuleActionKind.RedactRegex when action.RedactRegex is { } redact => ApplyRedact(logEvent, redact),
        _ => logEvent,
    };

    private static LogEvent ApplyExtract(LogEvent logEvent, ExtractRegexAction extract)
    {
        var source = ReadSource(logEvent, extract.SourceAttributeKey);
        if (source is null || GetRegex(extract.Pattern) is not { } regex)
        {
            return logEvent;
        }

        Match match;
        try
        {
            match = regex.Match(source);
        }
        catch (RegexMatchTimeoutException)
        {
            return logEvent;
        }

        if (!match.Success)
        {
            return logEvent;
        }

        var groupNames = regex.GetGroupNames();
        Dictionary<string, string>? extracted = null;
        foreach (var name in groupNames)
        {
            if (int.TryParse(name, out _))
            {
                continue; // positional group, not named - nothing to key an attribute on.
            }

            var group = match.Groups[name];
            if (!group.Success)
            {
                continue;
            }

            extracted ??= new Dictionary<string, string>(logEvent.LogAttributes);
            extracted[name] = group.Value;
        }

        return extracted is null ? logEvent : logEvent with { LogAttributes = extracted };
    }

    private static LogEvent ApplyRedact(LogEvent logEvent, RedactRegexAction redact)
    {
        var source = ReadSource(logEvent, redact.SourceAttributeKey);
        if (source is null || GetRegex(redact.Pattern) is not { } regex)
        {
            return logEvent;
        }

        string redacted;
        try
        {
            redacted = regex.Replace(source, redact.Replacement);
        }
        catch (RegexMatchTimeoutException)
        {
            return logEvent;
        }

        if (redacted == source)
        {
            return logEvent;
        }

        return WriteSource(logEvent, redact.SourceAttributeKey, redacted);
    }

    /// <summary><see langword="null"/> = <c>Body</c>, matching <see cref="ExtractRegexAction.SourceAttributeKey"/>/<see cref="RedactRegexAction.SourceAttributeKey"/>'s own doc comments.</summary>
    private static string? ReadSource(LogEvent logEvent, string? attributeKey) => attributeKey is null
        ? logEvent.Body
        : logEvent.LogAttributes.GetValueOrDefault(attributeKey);

    private static LogEvent WriteSource(LogEvent logEvent, string? attributeKey, string value)
    {
        if (attributeKey is null)
        {
            return logEvent with { Body = value };
        }

        var attributes = new Dictionary<string, string>(logEvent.LogAttributes) { [attributeKey] = value };
        return logEvent with { LogAttributes = attributes };
    }

    private static Regex? GetRegex(string pattern) => CompiledPatterns.GetOrAdd(pattern, static p =>
    {
        try
        {
            return new Regex(p, RegexOptions.None, TimeSpan.FromMilliseconds(100));
        }
        catch (ArgumentException)
        {
            return null;
        }
    });
}
