namespace Flare.Api.Model;

/// <summary>
/// Response of <c>GET /api/usage?days=7</c> - the Settings &gt; Usage page: what is driving
/// storage, rolled up from data the Ingestion, Indexing and Metrics-catalog pages already
/// read. Plain JSON only (no MemoryPack): a handful of small, bounded rows per request.
/// </summary>
public sealed record UsageResponse(
    DateTimeOffset GeneratedAt,
    int Days,
    IReadOnlyList<UsageSignalSummary> Signals,
    IReadOnlyList<UsageDayPoint> Daily,
    IReadOnlyList<UsageServiceRow> Services,
    IReadOnlyList<UsageAttributeRow> Attributes,
    long AttributeSampleRows,
    IReadOnlyList<UsageIngestKeyRow> IngestKeys);

/// <summary>One signal's stored events in the window and its table(s)' current compressed size.</summary>
public sealed record UsageSignalSummary(string Signal, long Events, long CompressedBytes);

/// <summary>Stored events for one signal on one UTC day.</summary>
public sealed record UsageDayPoint(DateTimeOffset Day, string Signal, long Events);

/// <summary>
/// One service's stored events for one signal in the window. <see cref="EstimatedBytes"/> is
/// the signal's compressed table size apportioned by this service's share of the window's
/// events - an estimate (ClickHouse has no per-service size), good for ranking, not billing.
/// </summary>
public sealed record UsageServiceRow(string ServiceName, string Signal, long Events, long EstimatedBytes);

/// <summary>
/// One attribute key's footprint in a bounded sample of recent rows: key+value bytes
/// (uncompressed) and how many sampled rows carried it. Scope is <c>Log</c>/<c>Resource</c>/<c>Span</c>/<c>DataPoint</c>.
/// </summary>
public sealed record UsageAttributeRow(string Signal, string Scope, string Key, long SampledBytes, long SampledOccurrences);

/// <summary>An active ingest key's accepted volume in the current UTC day (the only window Redis keeps, ADR-0051).</summary>
public sealed record UsageIngestKeyRow(Guid Id, string Name, long EventsToday, long BytesToday);
