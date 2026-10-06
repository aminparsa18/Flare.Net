namespace Flare.Ingest.Pipeline.LogMetrics;

/// <summary>Tuning knobs for log-based metrics. Bound from the <c>LogMetrics</c> configuration section.</summary>
public sealed class LogMetricOptions
{
    public const string SectionName = "LogMetrics";

    /// <summary>Master on/off switch - <see langword="false"/> stops the poll loop and no log metric is emitted.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often the definition set is re-polled; a new/edited/disabled definition takes up to this long to take effect on this replica.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Width of the time buckets counts are grouped into. Events are bucketed by their own timestamp, not the flush time.</summary>
    public TimeSpan BucketSize { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Distinct group-by value combinations one definition may emit per flush batch. Once reached,
    /// events with a new combination are counted under <see cref="LogMetricAggregator.OverflowValue"/>
    /// instead, so a runaway attribute (a request ID) cannot create unbounded series.
    /// </summary>
    public int MaxGroupsPerDefinition { get; set; } = 1000;
}
