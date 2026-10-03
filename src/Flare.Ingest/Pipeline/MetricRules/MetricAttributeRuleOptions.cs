namespace Flare.Ingest.Pipeline.MetricRules;

/// <summary>Tuning knobs for metric attribute reduction. Bound from the <c>MetricAttributeRules</c> configuration section.</summary>
public sealed class MetricAttributeRuleOptions
{
    public const string SectionName = "MetricAttributeRules";

    /// <summary>Master on/off switch - <see langword="false"/> stops the poll loop and leaves every metric point untouched.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the rule set is re-polled; a new/edited/disabled rule takes up to this long to take effect on this replica.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);
}
