namespace Flare.Ingest.Pipeline.MetricRules;

/// <summary>Mirror of <c>Flare.Api.Model.MetricAttributeRuleMode</c> - persisted as the <c>Mode</c> byte, so the numeric values are part of the contract.</summary>
public enum MetricAttributeRuleMode
{
    Drop = 0,
    KeepOnly = 1,
}

/// <summary>
/// One enabled <c>metric_attribute_rules</c> row, as <c>Flare.Ingest</c> needs it. Mirrors
/// rather than references <c>Flare.Api.Model.MetricAttributeRule</c> - same boundary
/// <see cref="Rules.PipelineRule"/> draws.
/// </summary>
public sealed record MetricAttributeRule
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Exact metric name, or a prefix with one trailing <c>*</c>.</summary>
    public required string MetricName { get; init; }

    public required MetricAttributeRuleMode Mode { get; init; }

    public required IReadOnlyList<string> Attributes { get; init; }
}
