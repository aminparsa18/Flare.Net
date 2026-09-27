using System.Collections.Concurrent;
using System.Text.Json;
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
/// A <see cref="RuleActionKind.ParseJson"/> source that isn't a JSON object (plain text,
/// malformed, an array/scalar at the root, or nested past <see cref="JsonDocument"/>'s own
/// 64-level limit) is likewise a no-op for that event.
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
        RuleActionKind.ParseJson when action.ParseJson is { } parse => ApplyParseJson(logEvent, parse),
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

    private static LogEvent ApplyParseJson(LogEvent logEvent, ParseJsonAction parse)
    {
        var source = ReadSource(logEvent, parse.SourceAttributeKey);
        if (source is null || !source.AsSpan().TrimStart().StartsWith("{"))
        {
            return logEvent; // Cheap pre-check - most bodies are plain text, not worth a parse attempt.
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(source);
        }
        catch (JsonException)
        {
            return logEvent;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return logEvent;
            }

            var maxDepth = Math.Clamp(parse.MaxDepth ?? ParseJsonAction.DefaultMaxDepth, 1, ParseJsonAction.MaxDepthLimit);
            var maxKeys = Math.Clamp(parse.MaxKeys ?? ParseJsonAction.DefaultMaxKeys, 1, ParseJsonAction.MaxKeysLimit);
            var attributes = new Dictionary<string, string>(logEvent.LogAttributes);
            var written = 0;
            Flatten(document.RootElement, parse.KeyPrefix ?? "", 1, maxDepth, maxKeys, attributes, ref written);
            return written == 0 ? logEvent : logEvent with { LogAttributes = attributes };
        }
    }

    /// <summary>Depth-first, document order - so when <paramref name="maxKeys"/> cuts off, it's the later keys that are dropped.</summary>
    private static void Flatten(JsonElement obj, string prefix, int depth, int maxDepth, int maxKeys, Dictionary<string, string> into, ref int written)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (written >= maxKeys)
            {
                return;
            }

            if (property.Name.Length == 0)
            {
                continue;
            }

            var key = prefix + property.Name;
            var value = property.Value;
            switch (value.ValueKind)
            {
                case JsonValueKind.Object when depth < maxDepth:
                    Flatten(value, key + ".", depth + 1, maxDepth, maxKeys, into, ref written);
                    break;
                case JsonValueKind.Null:
                    break;
                case JsonValueKind.String:
                    into[key] = value.GetString()!;
                    written++;
                    break;
                default: // Number/True/False, arrays, and objects past maxDepth - raw JSON text.
                    into[key] = value.GetRawText();
                    written++;
                    break;
            }
        }
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
