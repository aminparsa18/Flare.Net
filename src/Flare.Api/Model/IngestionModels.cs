using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Mirrors <c>Flare.Ingest.Stats.IngestionSignal</c> - deliberately duplicated rather than
/// shared via a project reference, same "different deployables, no reference between them"
/// precedent every other Flare.Ingest/Flare.Api pairing in this repo already follows (they
/// only ever agree by convention on the ClickHouse schema and, here, the Redis key/field
/// format - see <see cref="Query.IngestionStatsKeys"/>).
/// </summary>
public enum IngestionSignal
{
    Logs,
    Traces,
    Metrics,
}

/// <summary>Mirrors <c>Flare.Ingest.Stats.IngestionProtocol</c> - see <see cref="IngestionSignal"/>'s remarks.</summary>
public enum IngestionProtocol
{
    Grpc,
    Http,
    Scrape,
}

/// <summary>
/// <c>POST /api/ingestion/stats</c> request. <see cref="Minutes"/> drives both the chart
/// window and the "current window" totals (rejected/requests) below - deliberately one
/// window for both rather than a separate always-24h aggregate, to keep this to a single
/// Redis round trip per call. Defaults to 60 (matches <see cref="Query.IngestionStatsQueryService"/>'s
/// clamp), clamped server-side to [1, 1440] (24h, the buckets' own TTL ceiling).
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record IngestionStatsRequest(int Minutes = 60);

/// <summary>
/// One minute-bucket's counters for one (signal, protocol) pair. The response is dense -
/// every minute in the window carries an entry for all six (signal, protocol) pairs, zero-
/// filled where nothing arrived - so the dashboard never has to gap-fill by timestamp
/// itself.
/// </summary>
[MemoryPackable]
public sealed partial record IngestionBucketPoint(
    DateTimeOffset BucketStart,
    IngestionSignal Signal,
    IngestionProtocol Protocol,
    long Requests,
    long Records,
    long Bytes,
    long Rejected);

/// <summary>
/// One entry from the recent-errors list - a rejected export request, newest first.
/// <see cref="Signal"/>/<see cref="Protocol"/> stay plain strings end-to-end (matching how
/// <c>Flare.Ingest.Stats.IngestionErrorEntry</c> stores them), rather than round-tripping
/// through the enums above, since nothing here needs to switch on them structurally.
/// </summary>
[MemoryPackable]
public sealed partial record IngestionErrorEntryDto(
    DateTimeOffset Timestamp,
    string Signal,
    string Protocol,
    string Reason);

/// <summary>
/// The dashboard's summary tiles. <see cref="ArrivalsPerMinute"/>/<see cref="IngestedRecordsPerMinute"/>/
/// <see cref="IngestedBytesPerMinute"/> read the single most recent (possibly still-filling)
/// minute bucket in the window - "arrivals" is request count (an export call landed);
/// "ingested" is the records/bytes within those requests - a real distinction (an export
/// carries N records), not a cosmetic split of the same number the way Seq's "Current
/// Arrivals"/"Current Ingestion" tiles would be if mirrored literally.
/// <see cref="RejectedInWindow"/>/<see cref="RequestsInWindow"/> sum across the whole
/// requested window (see <see cref="IngestionStatsRequest.Minutes"/>'s remarks on why this
/// isn't a fixed 24h like Seq's own "last 24 hours" tiles).
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record IngestionStatsTotals(
    long ArrivalsPerMinute,
    long IngestedRecordsPerMinute,
    long IngestedBytesPerMinute,
    long RequestsInWindow,
    long RejectedInWindow);

[MemoryPackable]
public sealed partial record IngestionStatsResponse(
    DateTimeOffset GeneratedAt,
    int Minutes,
    IReadOnlyList<IngestionBucketPoint> Buckets,
    IngestionStatsTotals Totals,
    IReadOnlyList<IngestionErrorEntryDto> RecentErrors);
