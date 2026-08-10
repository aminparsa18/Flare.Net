namespace Flare.Ingest.Stats;

/// <summary>
/// Pure key/field-naming helpers for <see cref="RedisIngestionStatsTracker"/>, split out
/// so they're unit-testable without a real/mocked <c>IConnectionMultiplexer</c> - same
/// "test the pure function" precedent as <c>HistogramQuantileEstimator</c> and the various
/// SQL builders elsewhere in this repo.
/// </summary>
public static class IngestionStatsKeys
{
    private const string MinuteBucketPrefix = "flare:ingestion:minute:";

    /// <summary>
    /// The recent-errors list key - a capped (<see cref="MaxErrorEntries"/>) list of the
    /// most recent rejected-payload entries, newest first (<c>LPUSH</c>/<c>LTRIM</c>),
    /// giving the dashboard's "Ingestion Log" panel something to render without a
    /// dedicated ClickHouse table - deliberately in-memory/best-effort (same TTL as the
    /// minute buckets), not durable history, matching this feature's MVP scope.
    /// </summary>
    public const string ErrorsListKey = "flare:ingestion:errors";

    public const int MaxErrorEntries = 200;

    /// <summary>
    /// 25h, not 24h: gives a full rolling 24-hour window of usable history (matching
    /// Seq's "last 24 hours" ingestion stats) plus a 1-hour buffer so the oldest bucket
    /// a 24h query still wants hasn't already expired mid-read.
    /// </summary>
    public static readonly TimeSpan BucketTtl = TimeSpan.FromHours(25);

    /// <summary>
    /// One Redis hash per wall-clock minute, keyed by Unix-epoch minute number so it's
    /// naturally sortable/comparable as a plain integer and stable regardless of how the
    /// caller formats time. Each hash holds <c>{signal}:{protocol}:{requests|records|bytes|rejected}</c>
    /// counter fields, incremented via <c>HINCRBY</c>.
    /// </summary>
    public static string MinuteBucketKey(DateTimeOffset timestamp) =>
        MinuteBucketPrefix + timestamp.ToUnixTimeSeconds() / 60;

    /// <summary>
    /// The epoch-minute number a bucket key was built from, recovered by parsing back the
    /// suffix - lets a reader (Flare.Api) enumerate the last N minute keys without needing
    /// to duplicate this class, and lets tests assert round-trip stability.
    /// </summary>
    public static long ParseMinuteBucketKey(string key) =>
        long.Parse(key[MinuteBucketPrefix.Length..]);

    /// <summary>
    /// Lowercase <c>{signal}:{protocol}</c>, e.g. <c>logs:http</c> - readable directly via
    /// <c>redis-cli HGETALL</c> while debugging, same rationale <see cref="Pipeline.LogEventJsonContext"/>
    /// documents for its own plain-PascalCase choice.
    /// </summary>
    public static string FieldPrefix(IngestionSignal signal, IngestionProtocol protocol) =>
        $"{signal.ToString().ToLowerInvariant()}:{protocol.ToString().ToLowerInvariant()}";

    private const string ServiceRecordsPrefix = "flare:ingestion:service-records:";
    private const string ServiceBytesPrefix = "flare:ingestion:service-bytes:";

    /// <summary>
    /// One hash per (minute, signal), field = raw <c>service.name</c> (not folded into a
    /// composite field name the way <see cref="FieldPrefix"/> is) - a service name can
    /// itself legally contain a colon, and this avoids any delimiter-collision risk when
    /// parsing it back, at the cost of one key per signal per minute rather than one
    /// shared minute bucket. Records and bytes are separate hashes rather than packed into
    /// one field's value, for the same reason (HINCRBY only touches one number per field).
    /// </summary>
    public static string ServiceRecordsKey(DateTimeOffset timestamp, IngestionSignal signal) =>
        ServiceRecordsPrefix + timestamp.ToUnixTimeSeconds() / 60 + ":" + signal.ToString().ToLowerInvariant();

    public static string ServiceBytesKey(DateTimeOffset timestamp, IngestionSignal signal) =>
        ServiceBytesPrefix + timestamp.ToUnixTimeSeconds() / 60 + ":" + signal.ToString().ToLowerInvariant();

    /// <summary>Same key format as <see cref="ServiceRecordsKey"/>/<see cref="ServiceBytesKey"/>, built from an already-known epoch minute (the reader side already has one, from its own window loop).</summary>
    public static string ServiceRecordsKey(long epochMinute, IngestionSignal signal) =>
        ServiceRecordsPrefix + epochMinute + ":" + signal.ToString().ToLowerInvariant();

    public static string ServiceBytesKey(long epochMinute, IngestionSignal signal) =>
        ServiceBytesPrefix + epochMinute + ":" + signal.ToString().ToLowerInvariant();
}
