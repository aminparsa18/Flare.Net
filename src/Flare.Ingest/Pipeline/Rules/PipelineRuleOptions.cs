namespace Flare.Ingest.Pipeline.Rules;

/// <summary>Tuning knobs for the pipeline-rule engine. Bound from the <c>PipelineRules</c> configuration section, same convention as <c>Patterns.LogPatternOptions</c>.</summary>
public sealed class PipelineRuleOptions
{
    public const string SectionName = "PipelineRules";

    /// <summary>
    /// Master on/off switch. <see langword="false"/> makes <see cref="PipelineRuleAnnotator"/>
    /// a no-op and stops <see cref="PipelineRuleCache"/> from polling - same "immediate,
    /// config-only rollback path, no redeploy needed" role
    /// <c>Patterns.LogPatternOptions.Enabled</c> plays for Drain clustering.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often <see cref="PipelineRuleCache"/> re-polls <c>pipeline_rules</c> for changes. A newly created/edited/disabled rule takes up to this long to take effect on this replica.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
}
