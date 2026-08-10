namespace Flare.Ingest.Stats;

/// <summary>
/// Pure key-naming helpers for <see cref="RedisFlushHealthTracker"/> - same "split the
/// naming logic out so it's unit-testable without a real/mocked <c>IConnectionMultiplexer</c>"
/// precedent as <see cref="IngestionStatsKeys"/>.
/// </summary>
public static class FlushHealthKeys
{
    private const string Prefix = "flare:ingestion:flush:";

    /// <summary>
    /// One hash per signal (<see cref="IngestionSignal.Logs"/> -&gt; <c>ClickHouseFlushWorker</c>,
    /// <see cref="IngestionSignal.Traces"/> -&gt; <c>SpanFlushWorker</c>,
    /// <see cref="IngestionSignal.Metrics"/> -&gt; <c>MetricFlushWorker</c> - the pipeline
    /// layer calls the second one "spans", but this keys off the same <see cref="IngestionSignal"/>
    /// the Ingestion page's existing stats already use, not the pipeline's own naming, so
    /// the two features join on one vocabulary). No TTL, unlike the per-minute stats
    /// buckets: this always holds the *last known* flush outcome, and a worker that has
    /// stopped updating it (crashed, stuck) is exactly the case the dashboard needs to
    /// keep showing - an expiring key would silently blank that signal instead of
    /// surfacing it as stale.
    /// </summary>
    public static string HashKey(IngestionSignal signal) => Prefix + signal.ToString().ToLowerInvariant();

    public const string LastFlushAtField = "lastFlushAt";
    public const string LastBatchSizeField = "lastBatchSize";
    public const string LastErrorAtField = "lastErrorAt";
    public const string LastErrorField = "lastError";
    public const string ConsecutiveErrorsField = "consecutiveErrors";
}
