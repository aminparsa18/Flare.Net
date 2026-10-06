using Flare.Ingest.Pipeline.Rules;

namespace Flare.Ingest.Pipeline.LogMetrics;

/// <summary>
/// One enabled <c>log_metrics</c> row, as <c>Flare.Ingest</c> needs it. Mirrors rather than
/// references <c>Flare.Api.Model.LogMetric</c> - same boundary <see cref="PipelineRule"/> draws.
/// <see cref="Condition"/> reuses <see cref="PipelineRuleCondition"/>, so a log metric's scoping
/// has exactly the semantics of a pipeline rule's.
/// </summary>
public sealed record LogMetricDefinition
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    /// <summary>The emitted metric's name.</summary>
    public required string MetricName { get; init; }

    public required PipelineRuleCondition Condition { get; init; }

    /// <summary>Attribute keys that become data-point attributes; looked up in the log's own attributes, then its resource attributes.</summary>
    public required IReadOnlyList<string> GroupBy { get; init; }
}
