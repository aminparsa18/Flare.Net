namespace Flare.Ingest.Patterns;

/// <summary>
/// Tuning knobs for <see cref="DrainPatternMatcher"/>. Bound from the
/// <c>LogPattern</c> configuration section, same convention as
/// <see cref="Pipeline.LogEventPipelineOptions"/>.
/// </summary>
public sealed class LogPatternOptions
{
    public const string SectionName = "LogPattern";

    /// <summary>
    /// Master on/off switch. <see langword="false"/> makes <see cref="LogPatternAnnotator"/>
    /// a no-op (every row keeps the empty-string <c>PatternId</c>/<c>PatternTemplate</c>
    /// default) - an immediate, config-only rollback path if the matcher ever misbehaves
    /// in production, no redeploy or migration rollback needed.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Minimum fraction of matching token positions (matching-position-count / token-count)
    /// required for a body to merge into an existing cluster rather than start a new one.
    /// Classic Drain similarity threshold.
    /// </summary>
    public double SimilarityThreshold { get; set; } = 0.5;

    /// <summary>
    /// Hard cap on the number of live clusters across the whole tree. Bounds worst-case
    /// memory growth from adversarial/high-cardinality bodies - same safety-cap instinct
    /// as <see cref="Pipeline.LogEventPipelineOptions.StreamMaxLength"/> and
    /// <c>LogQueryService</c>'s ClickHouse <c>SafetyOptions()</c>. Once at the cap, creating
    /// a new cluster evicts the least-recently-used one tree-wide.
    /// </summary>
    public int MaxTemplates { get; set; } = 10_000;

    /// <summary>
    /// Body characters considered before masking/tokenization; excess is truncated so one
    /// pathologically long log line can't blow up flush-path latency.
    /// </summary>
    public int MaxBodyLength { get; set; } = 4096;
}
