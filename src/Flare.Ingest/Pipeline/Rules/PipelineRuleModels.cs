using System.Text.Json.Serialization;

namespace Flare.Ingest.Pipeline.Rules;

/// <summary>Which OTel attribute bag an <see cref="AttributeCondition"/> targets. Mirrors <c>Flare.Api.Model.AttributeBag</c> field-for-field - see that type's remarks for why this boundary mirrors rather than references.</summary>
public enum AttributeBag
{
    Log,
    Resource,
    Scope,
}

/// <summary>Mirrors <c>Flare.Api.Model.AttributeFilterOperator</c> field-for-field (including ordinal order - this deserializes via <see cref="JsonStringEnumConverter"/>, by name not ordinal, but kept identical for clarity).</summary>
public enum AttributeConditionOperator
{
    Equals,
    NotEquals,
    Exists,
    Absent,
    Regex,
    NotRegex,
    In,
    NotIn,
}

/// <summary>Mirrors <c>Flare.Api.Model.AttributeFilter</c> - one condition against a <see cref="Model.LogEvent"/> attribute bag.</summary>
public sealed record AttributeCondition
{
    public AttributeBag Bag { get; init; } = AttributeBag.Log;

    public string Key { get; init; } = "";

    public string Value { get; init; } = "";

    public AttributeConditionOperator Operator { get; init; } = AttributeConditionOperator.Equals;

    public IReadOnlyList<string>? Values { get; init; }
}

/// <summary>
/// Mirrors the subset of <c>Flare.Api.Model.LogFilter</c> that's meaningful when matching
/// a live <see cref="Model.LogEvent"/> at ingest time - <c>From</c>/<c>To</c>/<c>TraceId</c>/
/// <c>SpanId</c>/<c>PatternId</c> are round-tripped in <c>ConditionJson</c> too but ignored
/// here (no time window on a live event; <c>PatternId</c> doesn't exist yet - this runs
/// before Drain clustering does). System.Text.Json skips unmapped JSON members by
/// default, so a full <c>LogFilter</c> payload deserializes cleanly into just these
/// fields.
/// </summary>
public sealed record PipelineRuleCondition
{
    public IReadOnlyList<string>? Services { get; init; }

    public IReadOnlyList<byte>? SeverityNumbers { get; init; }

    public string? Search { get; init; }

    public IReadOnlyList<AttributeCondition>? Attributes { get; init; }
}

/// <summary>Mirrors <c>Flare.Api.Model.RuleActionKind</c>.</summary>
public enum RuleActionKind
{
    ExtractRegex,
    RedactRegex,
}

/// <summary>Mirrors <c>Flare.Api.Model.ExtractRegexAction</c>.</summary>
public sealed record ExtractRegexAction
{
    public string? SourceAttributeKey { get; init; }

    public string Pattern { get; init; } = "";
}

/// <summary>Mirrors <c>Flare.Api.Model.RedactRegexAction</c>.</summary>
public sealed record RedactRegexAction
{
    public string? SourceAttributeKey { get; init; }

    public string Pattern { get; init; } = "";

    public string Replacement { get; init; } = "***";
}

/// <summary>Mirrors <c>Flare.Api.Model.PipelineRuleAction</c>.</summary>
public sealed record PipelineRuleAction
{
    public RuleActionKind Kind { get; init; }

    public ExtractRegexAction? ExtractRegex { get; init; }

    public RedactRegexAction? RedactRegex { get; init; }
}

/// <summary>
/// Mirrors the subset of <c>Flare.Api.Model.PipelineRule</c> <c>Flare.Ingest</c> actually
/// needs to evaluate and apply a rule - no <c>Description</c>, since nothing here logs or
/// displays it.
/// </summary>
public sealed record PipelineRule
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public required PipelineRuleCondition Condition { get; init; }

    public required IReadOnlyList<PipelineRuleAction> Actions { get; init; }
}

/// <summary>
/// Source-generated <see cref="System.Text.Json"/> contract for the <c>ConditionJson</c>/
/// <c>ActionsJson</c> columns <see cref="ClickHousePipelineRuleStore"/> reads - camelCase,
/// string enums, matching exactly what <c>Flare.Api</c>'s <c>PipelineRulesJsonContext</c>
/// wrote (the two are a cross-service JSON contract, not a shared type - see
/// <see cref="PipelineRuleCondition"/>'s remarks).
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PipelineRuleCondition))]
[JsonSerializable(typeof(IReadOnlyList<PipelineRuleAction>))]
public sealed partial class PipelineRuleJsonContext : JsonSerializerContext;
