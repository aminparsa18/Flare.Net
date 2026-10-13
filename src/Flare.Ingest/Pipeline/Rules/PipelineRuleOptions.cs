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

    /// <summary>Managed PII/secret redaction (ADR-0199). Off by default; applied before user-defined rules, to <c>Body</c> and log attribute values.</summary>
    public PiiRedactionOptions Pii { get; set; } = new();
}

/// <summary>Bound from <c>PipelineRules:Pii</c>.</summary>
public sealed class PiiRedactionOptions
{
    /// <summary>One toggle for the whole built-in rule set. This is the configured default; an Admin's dashboard override (ADR-0210, <see cref="IPipelineRuleCache.PiiEnabledOverride"/>) wins over it within <see cref="PipelineRuleOptions.RefreshInterval"/>, with no restart.</summary>
    public bool Enabled { get; set; }

    /// <summary>Which detectors run. Defaults to all; narrow with e.g. <c>Email,CreditCard</c>.</summary>
    public PiiDetectors Detectors { get; set; } = PiiDetectors.All;

    /// <summary>The detector set that applies right now: <see cref="PiiDetectors.None"/> when redaction is off, after letting the dashboard override beat <see cref="Enabled"/>.</summary>
    public PiiDetectors EffectiveDetectors(bool? enabledOverride) => enabledOverride ?? Enabled ? Detectors : PiiDetectors.None;
}
