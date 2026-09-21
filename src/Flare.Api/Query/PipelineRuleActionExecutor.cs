using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Pure <see cref="LogEventDto"/> → <see cref="LogEventDto"/> action application - the
/// preview endpoint's counterpart to <c>Flare.Ingest.Pipeline.Rules.PipelineRuleExecutor</c>,
/// mirroring its <see cref="RuleActionKind.ExtractRegex"/>/<see cref="RuleActionKind.RedactRegex"/>
/// semantics field-for-field against <see cref="Flare.Api.Model.LogEventDto"/> instead of
/// <c>Flare.Ingest.Model.LogEvent</c>.
/// </summary>
/// <remarks>
/// Deliberately has no condition-matching counterpart to
/// <c>PipelineRuleConditionMatcher</c>/<see cref="LogFilterMatcher"/>: a preview only ever
/// evaluates one rule (saved or draft) against a sample already pulled via
/// <see cref="LogQueryService.SearchAsync"/> using that same rule's <c>Condition</c> as the
/// search filter, so every <see cref="LogEventDto"/> handed to <see cref="Apply"/> already
/// matches by construction - there's nothing left to re-check in memory. One accepted
/// consequence: the sample is scoped by ClickHouse's RE2-based <c>match()</c> (via
/// <c>LogFilterSqlBuilder</c>), not the exact <see cref="Regex"/> engine
/// <c>PipelineRuleConditionMatcher</c> uses at ingest time - the same engine-choice gap
/// <see cref="LogFilterMatcher"/>'s own remarks already document and accept for live-tail.
/// Same fail-closed posture as the Ingest-side executor: an invalid pattern or a timeout
/// skips that action (returns the event unchanged) rather than throwing, and compiled
/// <see cref="Regex"/> instances are cached per pattern string in a process-wide
/// <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// </remarks>
public static class PipelineRuleActionExecutor
{
    private static readonly ConcurrentDictionary<string, Regex?> CompiledPatterns = new();

    public static LogEventDto Apply(LogEventDto logEvent, IReadOnlyList<PipelineRuleAction> actions)
    {
        foreach (var action in actions)
        {
            logEvent = ApplyAction(logEvent, action);
        }

        return logEvent;
    }

    private static LogEventDto ApplyAction(LogEventDto logEvent, PipelineRuleAction action) => action.Kind switch
    {
        RuleActionKind.ExtractRegex when action.ExtractRegex is { } extract => ApplyExtract(logEvent, extract),
        RuleActionKind.RedactRegex when action.RedactRegex is { } redact => ApplyRedact(logEvent, redact),
        _ => logEvent,
    };

    private static LogEventDto ApplyExtract(LogEventDto logEvent, ExtractRegexAction extract)
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

    private static LogEventDto ApplyRedact(LogEventDto logEvent, RedactRegexAction redact)
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
    private static string? ReadSource(LogEventDto logEvent, string? attributeKey) => attributeKey is null
        ? logEvent.Body
        : logEvent.LogAttributes.GetValueOrDefault(attributeKey);

    private static LogEventDto WriteSource(LogEventDto logEvent, string? attributeKey, string value)
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
