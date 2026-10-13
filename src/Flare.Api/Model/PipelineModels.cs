using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// <c>GET /api/ingestion/pipeline</c> request. Only <see cref="Minutes"/> matters, and only
/// for the service-breakdown aggregation window - stream/consumer-group health (below) is
/// always a live snapshot, not windowed. Same default/clamp as
/// <see cref="IngestionStatsRequest"/>.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record PipelineStatsRequest(int Minutes = 60);

/// <summary>
/// One Redis Stream's live buffer/consumer-group depth for one signal. <see cref="Lag"/> is
/// XINFO GROUPS' own tracked "entries never yet delivered to this group" count - the
/// number that actually answers "is the pipeline falling behind" (see
/// <see cref="Query.PipelineQueryService"/>'s remarks for why this beats deriving it from
/// XLEN/XPENDING). Null when Redis can't compute it (StackExchange.Redis models the wire
/// value's "unknown" case as a null <c>long?</c> - happens if the stream's own
/// entries-added counter was reset, e.g. via <c>XSETID</c>, which this repo never calls
/// but a self-hosted admin could). <see cref="PendingCount"/>/<see cref="OldestPendingAgeSeconds"/>
/// answer a different question - "is something stuck" (delivered but never acked, e.g.
/// approaching the poison-message threshold each <c>*FlushWorker*</c> enforces).
/// <see cref="Available"/> is false only when the stream/group doesn't exist yet (no
/// traffic on this signal since Flare.Ingest last started) - not an error state.
/// <see cref="Capacity"/> is the configured MAXLEN this stream is trimmed to on every XADD
/// (<c>Query.PipelineStreamKeys.Capacity</c>) - <see cref="Length"/> approaching it is a
/// real risk, not just backpressure: Redis's approximate MAXLEN trims the *oldest*
/// unflushed entries once the cap is hit, which is silent data loss, the same "queue
/// size/capacity" signal OpenTelemetry Collector's own health guidance calls out alongside
/// accepted/refused data and exporter failures.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record PipelineStreamHealth(
    IngestionSignal Signal,
    string StreamKey,
    bool Available,
    long Length,
    long Capacity,
    long? Lag,
    long PendingCount,
    int Consumers,
    double? OldestPendingAgeSeconds);

/// <summary>
/// One <c>*FlushWorker</c>'s last-known flush outcome, read back from what
/// <c>Flare.Ingest.Stats.RedisFlushHealthTracker</c> writes once per flush cycle.
/// <see cref="LastFlushAt"/>/<see cref="LastBatchSize"/> only update on a *successful*
/// flush - they keep reflecting the last time data actually reached ClickHouse, not the
/// last attempt, so a stuck worker shows a growing gap here even while
/// <see cref="ConsecutiveErrors"/> climbs. All fields null/zero if this worker has never
/// flushed since Flare.Ingest last started (not an error state).
/// </summary>
[MemoryPackable]
public sealed partial record PipelineFlushHealth(
    IngestionSignal Signal,
    DateTimeOffset? LastFlushAt,
    long? LastBatchSize,
    DateTimeOffset? LastErrorAt,
    string? LastError,
    long ConsecutiveErrors);

/// <summary>
/// One service's share of a signal's traffic within the requested window.
/// <see cref="AverageClockSkewMs"/> is <c>(IngestedAt - event time)</c> averaged across
/// every record this service sent in the window (see ADR-0014) - positive means this
/// service's events typically arrive claiming a time in the past relative to receipt
/// (the expected case: latency), negative means they claim a time in the future (the
/// service's clock is ahead of this server's). A simple per-window mean, not a robust
/// statistic - see <c>Flare.Ingest.Stats.ServiceAcceptedCounts.SkewNanosSum</c>'s
/// remarks for the same caveat on the write side.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record PipelineServiceEntry(string ServiceName, long Records, long Bytes, double AverageClockSkewMs);

/// <summary>
/// Per-<c>service.name</c> breakdown for one signal, capped to the top
/// <see cref="Query.PipelineQueryService.TopServicesPerSignal"/> services by record count -
/// same "+N more" convention <c>MetricChart</c>/<c>IndexingGrowthChart</c> already use for
/// bounding a legend, not a silent drop. <see cref="Bytes"/> figures are a documented
/// approximation (see <c>Flare.Ingest.Stats.ServiceBreakdown</c>'s remarks) - OTLP gives one
/// byte count per export request, not per resource, so a request spanning multiple services
/// has its bytes split proportionally to each service's record share, not measured exactly.
/// </summary>
[MemoryPackable]
public sealed partial record PipelineServiceBreakdown(
    IngestionSignal Signal,
    IReadOnlyList<PipelineServiceEntry> TopServices,
    long OtherServiceCount,
    long OtherRecords,
    long OtherBytes);

[MemoryPackable]
public sealed partial record PipelineStatsResponse(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<PipelineStreamHealth> Streams,
    IReadOnlyList<PipelineFlushHealth> FlushWorkers,
    IReadOnlyList<PipelineServiceBreakdown> ServiceBreakdowns);
