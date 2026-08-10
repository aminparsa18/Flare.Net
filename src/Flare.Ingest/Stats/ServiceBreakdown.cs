namespace Flare.Ingest.Stats;

/// <summary>One service's share of an accepted export request's records/bytes.</summary>
public readonly record struct ServiceAcceptedCounts(int RecordCount, long ByteCount);

/// <summary>
/// Pure grouping logic behind the per-<c>service.name</c> breakdown on the Ingestion
/// page's pipeline-health section (Planning.md v10) - split out so it's unit-testable
/// without a real/mocked <c>IConnectionMultiplexer</c>, same "test the pure function"
/// precedent as <see cref="IngestionStatsKeys"/>/<see cref="FlushHealthKeys"/>.
/// </summary>
public static class ServiceBreakdown
{
    /// <summary>
    /// Matches the OTel SDK's own default resource attribute for an unset
    /// <c>service.name</c> (confirmed against a real SDK run in the v4 trace work - see
    /// Planning.md), so unattributed traffic groups under the same label a user would
    /// already recognize from elsewhere in the app, not a made-up placeholder.
    /// </summary>
    public const string UnknownServiceName = "unknown_service";

    /// <summary>
    /// Groups <paramref name="serviceNames"/> (one entry per accepted record, null/empty
    /// falling back to <see cref="UnknownServiceName"/>) and splits
    /// <paramref name="totalByteCount"/> proportionally to each service's share of the
    /// total record count. This is a deliberate approximation, not exact accounting: OTLP
    /// gives one byte count for the whole export request, not per resource, so there is no
    /// way to attribute bytes exactly when a single request carries multiple services'
    /// records (a real, common shape - e.g. a collector fanning in from several apps).
    /// </summary>
    public static Dictionary<string, ServiceAcceptedCounts> Build(
        IEnumerable<string?> serviceNames,
        long totalByteCount)
    {
        var recordCounts = new Dictionary<string, int>();
        var total = 0;
        foreach (var name in serviceNames)
        {
            var key = string.IsNullOrEmpty(name) ? UnknownServiceName : name;
            recordCounts[key] = recordCounts.GetValueOrDefault(key) + 1;
            total++;
        }

        var result = new Dictionary<string, ServiceAcceptedCounts>(recordCounts.Count);
        foreach (var (service, recordCount) in recordCounts)
        {
            var byteShare = total == 0 ? 0 : totalByteCount * recordCount / total;
            result[service] = new ServiceAcceptedCounts(recordCount, byteShare);
        }

        return result;
    }
}
