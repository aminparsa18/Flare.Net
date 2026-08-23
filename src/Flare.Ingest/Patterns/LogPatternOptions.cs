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
    /// a new cluster evicts the least-recently-used one tree-wide. Only enforced by
    /// <see cref="InMemoryPatternClusterStore"/> - <see cref="RedisPatternClusterStore"/>
    /// bounds growth via <see cref="SharedTemplateTtl"/> instead (see its remarks).
    /// </summary>
    public int MaxTemplates { get; set; } = 10_000;

    /// <summary>
    /// Body characters considered before masking/tokenization; excess is truncated so one
    /// pathologically long log line can't blow up flush-path latency.
    /// </summary>
    public int MaxBodyLength { get; set; } = 4096;

    /// <summary>
    /// <see langword="false"/> (default): <see cref="InMemoryPatternClusterStore"/>, a
    /// per-process tree - correct for a single <c>Flare.Ingest</c> replica, no Redis
    /// traffic added. <see langword="true"/>: <see cref="RedisPatternClusterStore"/>,
    /// shared across replicas - the fix for docs/clustering.md's cross-replica
    /// <c>PatternId</c> fragmentation; set on <c>ingest-1</c>/<c>ingest-2</c> in
    /// <c>docker-compose.cluster.yml</c>. Same "config-gated, off by default" shape as
    /// <c>ClickHouse:ClusterMode</c>.
    /// </summary>
    public bool SharedStore { get; set; }

    /// <summary>
    /// Sliding TTL <see cref="RedisPatternClusterStore"/> refreshes on a bucket key every
    /// time it's touched; unused by <see cref="InMemoryPatternClusterStore"/>. A
    /// rarely-touched template's key simply expires - the shared-store equivalent of
    /// <see cref="MaxTemplates"/>'s LRU eviction, without needing a cross-replica-visible
    /// counter/sorted-set touched on every write.
    /// </summary>
    public TimeSpan SharedTemplateTtl { get; set; } = TimeSpan.FromHours(72);
}
