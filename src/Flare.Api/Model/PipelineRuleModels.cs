using System.Text.RegularExpressions;
using MemoryPack;

namespace Flare.Api.Model;

/// <summary>Which action a <see cref="PipelineRuleAction"/> performs.</summary>
public enum RuleActionKind
{
    /// <summary>Pulls named regex capture groups out of a source field into new <c>LogAttributes</c> entries. See <see cref="PipelineRuleAction.ExtractRegex"/>.</summary>
    ExtractRegex,

    /// <summary>Replaces every regex match in a source field with a fixed mask. See <see cref="PipelineRuleAction.RedactRegex"/>.</summary>
    RedactRegex,
}

/// <summary>
/// <see cref="RuleActionKind.ExtractRegex"/>'s config: match <see cref="Pattern"/> against
/// <see cref="SourceAttributeKey"/> (or <c>Body</c> when null) and add one new
/// <c>LogAttributes</c> entry per named capture group the pattern defines - the group's
/// name becomes the attribute key, its matched text becomes the value. A pattern with no
/// named groups (only positional ones) extracts nothing; see
/// <see cref="PipelineRuleRequest.Validate"/>.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record ExtractRegexAction
{
    /// <summary>Attribute key in the <c>Log</c> bag to read from; <see langword="null"/> (the default) reads <c>Body</c> instead.</summary>
    public string? SourceAttributeKey { get; init; }

    public required string Pattern { get; init; }
}

/// <summary>
/// <see cref="RuleActionKind.RedactRegex"/>'s config: replace every match of
/// <see cref="Pattern"/> in <see cref="SourceAttributeKey"/> (or <c>Body</c> when null)
/// with <see cref="Replacement"/>.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record RedactRegexAction
{
    /// <summary>Attribute key in the <c>Log</c> bag to redact; <see langword="null"/> (the default) redacts <c>Body</c> instead.</summary>
    public string? SourceAttributeKey { get; init; }

    public required string Pattern { get; init; }

    public string Replacement { get; init; } = "***";
}

/// <summary>
/// One step of a <see cref="PipelineRule"/>'s ordered action list. One flat record with
/// nullable kind-specific groups gated by <see cref="Kind"/>, same discriminator shape
/// <see cref="AlertRule"/>'s <see cref="AlertConditionKind"/>/<see cref="AlertRule.MetricCondition"/>/
/// <see cref="AlertRule.ExceptionCondition"/> already use, rather than polymorphic JSON.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record PipelineRuleAction
{
    public required RuleActionKind Kind { get; init; }

    /// <summary>Set (non-null) only when <see cref="Kind"/> is <see cref="RuleActionKind.ExtractRegex"/>; null/ignored otherwise.</summary>
    public ExtractRegexAction? ExtractRegex { get; init; }

    /// <summary>Set (non-null) only when <see cref="Kind"/> is <see cref="RuleActionKind.RedactRegex"/>; null/ignored otherwise.</summary>
    public RedactRegexAction? RedactRegex { get; init; }
}

/// <summary>
/// A saved field-extraction/redaction rule, applied by <c>Flare.Ingest</c>'s
/// <c>PipelineRuleAnnotator</c> at flush time - the same seam Drain pattern clustering
/// runs at, but earlier in the batch (redact/extract before a cluster template is
/// computed off <c>Body</c>, so a redacted body is what gets clustered).
/// </summary>
/// <remarks>
/// <see cref="Condition"/> reuses <see cref="LogFilter"/> verbatim, same as
/// <see cref="AlertRule.Condition"/> - <see cref="LogFilter.From"/>/<see cref="LogFilter.To"/>/
/// <see cref="LogFilter.PatternId"/> are ignored when matching at ingest time: there's no
/// time window on a live event, and <c>PatternId</c> doesn't exist yet (this runs before
/// Drain clustering does). An empty <see cref="Condition"/> (every field null/empty)
/// deliberately matches every log - the dashboard form always shows an explicit "matches
/// all logs" notice in that case, rather than letting an unscoped rule be a silent
/// default (see docs-internal/adr/0033-pipeline-rules-extraction-redaction.md).
/// </remarks>
[MemoryPackable]
public sealed partial record PipelineRule
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public bool Enabled { get; init; } = true;

    public required LogFilter Condition { get; init; }

    /// <summary>Applied in list order against a matching event; each action sees the previous action's output.</summary>
    public required IReadOnlyList<PipelineRuleAction> Actions { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Create/update request body for <c>/api/pipeline-rules</c>.</summary>
/// <remarks>
/// Optional members are nullable rather than typed with a non-default C# initializer,
/// same reasoning as <see cref="AlertRuleRequest"/>'s doc comment - a non-nullable
/// <see cref="bool"/> can't tell "omitted" apart from "explicitly false" once System.Text.Json's
/// source-gen constructor-style converter defaults an omitted member.
/// </remarks>
[MemoryPackable]
public sealed partial record PipelineRuleRequest
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public bool? Enabled { get; init; }

    /// <summary>See <see cref="Model.LogSearchRequest.Filter"/>'s doc comment - the same JSON-deserialization default caveat applies here.</summary>
    public LogFilter Condition { get; init; } = new();

    public IReadOnlyList<PipelineRuleAction>? Actions { get; init; }

    /// <summary>
    /// At least one action, each with exactly its <see cref="RuleActionKind"/>'s matching
    /// group set, a syntactically valid <see cref="Regex"/> pattern, and (for
    /// <see cref="RuleActionKind.ExtractRegex"/>) at least one named capture group - a
    /// pattern with only positional groups would silently extract nothing. Called from
    /// <c>PipelineRuleEndpoints</c>'s create/update handlers. Returns an error message, or
    /// null when this request is valid.
    /// </summary>
    public string? Validate()
    {
        if (Actions is not { Count: > 0 })
        {
            return "At least one action is required.";
        }

        for (var i = 0; i < Actions.Count; i++)
        {
            var action = Actions[i];
            if (action.Kind == RuleActionKind.ExtractRegex)
            {
                if (action.ExtractRegex is null || action.RedactRegex is not null)
                {
                    return $"actions[{i}]: extractRegex must be set (and redactRegex unset) when kind is ExtractRegex.";
                }

                if (!TryCompile(action.ExtractRegex.Pattern, out var regex, out var error))
                {
                    return $"actions[{i}]: {error}";
                }

                if (regex.GetGroupNames().All(n => int.TryParse(n, out _)))
                {
                    return $"actions[{i}]: pattern must define at least one named capture group, e.g. (?<field>...).";
                }
            }
            else if (action.Kind == RuleActionKind.RedactRegex)
            {
                if (action.RedactRegex is null || action.ExtractRegex is not null)
                {
                    return $"actions[{i}]: redactRegex must be set (and extractRegex unset) when kind is RedactRegex.";
                }

                if (!TryCompile(action.RedactRegex.Pattern, out _, out var error))
                {
                    return $"actions[{i}]: {error}";
                }
            }
        }

        return null;
    }

    private static bool TryCompile(string pattern, out Regex regex, out string error)
    {
        try
        {
            regex = new Regex(pattern, RegexOptions.None, TimeSpan.FromMilliseconds(100));
            error = "";
            return true;
        }
        catch (ArgumentException ex)
        {
            regex = null!;
            error = $"invalid pattern: {ex.Message}";
            return false;
        }
    }
}

/// <summary>Response body for <c>GET /api/pipeline-rules</c>.</summary>
[MemoryPackable]
public sealed partial record PipelineRuleListResponse
{
    public required IReadOnlyList<PipelineRule> Rules { get; init; }
}

/// <summary>
/// One sampled log's before/after preview - see
/// <see cref="PipelineRulePreviewResult"/>'s remarks. <see cref="BeforeAttributes"/>/
/// <see cref="AfterAttributes"/> carry only the <c>Log</c> attribute bag, the one
/// <see cref="RuleActionKind.ExtractRegex"/>/<see cref="RuleActionKind.RedactRegex"/> can
/// ever touch (see <c>ExtractRegexAction.SourceAttributeKey</c>'s doc comment) - Resource/
/// Scope attributes are never mutated, so showing them here would only add noise.
/// </summary>
[MemoryPackable]
public sealed partial record PipelineRulePreviewMatch
{
    public required Guid EventId { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required string ServiceName { get; init; }

    public required string BeforeBody { get; init; }

    public required string AfterBody { get; init; }

    public required IReadOnlyDictionary<string, string> BeforeAttributes { get; init; }

    public required IReadOnlyDictionary<string, string> AfterAttributes { get; init; }

    /// <summary><see langword="true"/> when at least one action actually changed <c>Body</c> or a <c>Log</c> attribute for this event - a rule can match a log (see <see cref="PipelineRule.Condition"/>) without any action having an effect on it, e.g. a redact pattern that doesn't occur in this particular sample.</summary>
    public required bool Changed { get; init; }
}

/// <summary>
/// Response body for <c>POST /api/pipeline-rules/{id}/preview</c> and
/// <c>POST /api/pipeline-rules/preview</c> - a dry-run of a saved or draft rule's actions
/// against a bounded, most-recent-first sample of logs already matching its own
/// <see cref="PipelineRule.Condition"/> (pulled via
/// <see cref="Query.LogQueryService.SearchAsync"/>, capped at
/// <see cref="Endpoints.PipelineRuleEndpoints.PreviewSampleSize"/> rows), so a rule's actions
/// can be verified before it starts mutating real ingest traffic. Never writes anything -
/// mirrors <c>AlertEndpoints</c>'s <c>AlertTestResult</c> dry-run shape for the same reason.
/// </summary>
[MemoryPackable]
public sealed partial record PipelineRulePreviewResult
{
    /// <summary>How many logs the sample pulled - at most <see cref="Endpoints.PipelineRuleEndpoints.PreviewSampleSize"/>, fewer if the condition (and the search's default lookback window, when <c>Condition.From</c>/<c>To</c> are unset) doesn't match that many.</summary>
    public required int SampledCount { get; init; }

    /// <summary>How many of <see cref="SampledCount"/> had at least one <see cref="PipelineRulePreviewMatch.Changed"/> effect.</summary>
    public required int ChangedCount { get; init; }

    public required IReadOnlyList<PipelineRulePreviewMatch> Matches { get; init; }
}
