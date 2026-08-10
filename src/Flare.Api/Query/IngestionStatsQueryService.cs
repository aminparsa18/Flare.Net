using System.Text.Json;
using Flare.Api.Model;
using StackExchange.Redis;

namespace Flare.Api.Query;

public interface IIngestionStatsQueryService
{
    Task<IngestionStatsResponse> GetStatsAsync(IngestionStatsRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads back what <c>Flare.Ingest.Stats.RedisIngestionStatsTracker</c> writes: one
/// <c>HGETALL</c> per minute bucket in the requested window plus one <c>LRANGE</c> for the
/// recent-errors list, all dispatched as a single <see cref="IDatabase.CreateBatch"/> round
/// trip (same rationale the write side documents - this runs on every dashboard poll, not
/// just once). The actual shaping into <see cref="IngestionStatsResponse"/> is split into
/// static, Redis-free methods below so it's unit-testable directly against hand-built
/// <see cref="HashEntry"/>/<see cref="RedisValue"/> data, same "test the pure function"
/// precedent as <c>HistogramQuantileEstimator</c>/the various SQL builders.
/// </summary>
public sealed class IngestionStatsQueryService(
    IConnectionMultiplexer connectionMultiplexer,
    TimeProvider timeProvider) : IIngestionStatsQueryService
{
    private static readonly IngestionSignal[] Signals = Enum.GetValues<IngestionSignal>();
    private static readonly IngestionProtocol[] Protocols = Enum.GetValues<IngestionProtocol>();

    public async Task<IngestionStatsResponse> GetStatsAsync(IngestionStatsRequest request, CancellationToken cancellationToken = default)
    {
        var minutes = ClampMinutes(request.Minutes);
        var now = timeProvider.GetUtcNow();
        var nowMinute = now.ToUnixTimeSeconds() / 60;
        var startMinute = nowMinute - (minutes - 1);

        var db = connectionMultiplexer.GetDatabase();
        var batch = db.CreateBatch();

        var hashTasks = new Task<HashEntry[]>[minutes];
        for (var i = 0; i < minutes; i++)
        {
            hashTasks[i] = batch.HashGetAllAsync(IngestionStatsKeys.MinuteBucketKey(startMinute + i));
        }

        var errorsTask = batch.ListRangeAsync(IngestionStatsKeys.ErrorsListKey, 0, IngestionStatsKeys.MaxErrorEntries - 1);
        batch.Execute();

        await Task.WhenAll(hashTasks).WaitAsync(cancellationToken);
        var rawErrors = await errorsTask.WaitAsync(cancellationToken);

        var minuteHashes = new Dictionary<long, HashEntry[]>(minutes);
        for (var i = 0; i < minutes; i++)
        {
            minuteHashes[startMinute + i] = hashTasks[i].Result;
        }

        var buckets = BuildBuckets(startMinute, minutes, minuteHashes);
        var totals = BuildTotals(buckets, nowMinute);
        var recentErrors = BuildRecentErrors(rawErrors);

        return new IngestionStatsResponse(now, minutes, buckets, totals, recentErrors);
    }

    internal static int ClampMinutes(int requested) =>
        Math.Clamp(requested <= 0 ? 60 : requested, 1, 1440);

    internal static IReadOnlyList<IngestionBucketPoint> BuildBuckets(
        long startMinute,
        int minutes,
        IReadOnlyDictionary<long, HashEntry[]> minuteHashes)
    {
        var result = new List<IngestionBucketPoint>(minutes * Signals.Length * Protocols.Length);
        for (var i = 0; i < minutes; i++)
        {
            var minute = startMinute + i;
            var bucketStart = DateTimeOffset.FromUnixTimeSeconds(minute * 60);
            var fields = minuteHashes.TryGetValue(minute, out var hash) ? ToFieldMap(hash) : [];

            foreach (var signal in Signals)
            {
                foreach (var protocol in Protocols)
                {
                    var prefix = IngestionStatsKeys.FieldPrefix(signal, protocol);
                    result.Add(new IngestionBucketPoint(
                        bucketStart,
                        signal,
                        protocol,
                        Requests: GetField(fields, $"{prefix}:requests"),
                        Records: GetField(fields, $"{prefix}:records"),
                        Bytes: GetField(fields, $"{prefix}:bytes"),
                        Rejected: GetField(fields, $"{prefix}:rejected")));
                }
            }
        }

        return result;
    }

    internal static IngestionStatsTotals BuildTotals(IReadOnlyList<IngestionBucketPoint> buckets, long currentMinute)
    {
        var currentBucketStart = DateTimeOffset.FromUnixTimeSeconds(currentMinute * 60);
        long arrivals = 0, records = 0, bytes = 0, requestsInWindow = 0, rejectedInWindow = 0;

        foreach (var bucket in buckets)
        {
            requestsInWindow += bucket.Requests;
            rejectedInWindow += bucket.Rejected;

            if (bucket.BucketStart == currentBucketStart)
            {
                arrivals += bucket.Requests;
                records += bucket.Records;
                bytes += bucket.Bytes;
            }
        }

        return new IngestionStatsTotals(arrivals, records, bytes, requestsInWindow, rejectedInWindow);
    }

    internal static IReadOnlyList<IngestionErrorEntryDto> BuildRecentErrors(RedisValue[] rawEntries)
    {
        var result = new List<IngestionErrorEntryDto>(rawEntries.Length);
        foreach (var raw in rawEntries)
        {
            if (raw.IsNullOrEmpty)
            {
                continue;
            }

            try
            {
                var entry = JsonSerializer.Deserialize((string)raw!, IngestionErrorWireJsonContext.Default.IngestionErrorEntryDto);
                if (entry is not null)
                {
                    result.Add(entry);
                }
            }
            catch (JsonException)
            {
                // A malformed entry in the recent-errors list shouldn't take down the whole
                // stats response - skip it and keep the rest, same "don't fail wholesale over
                // one bad item" precedent OtlpMetricsMapper uses for unsupported point types.
            }
        }

        return result;
    }

    private static Dictionary<string, long> ToFieldMap(HashEntry[] hash)
    {
        var map = new Dictionary<string, long>(hash.Length);
        foreach (var entry in hash)
        {
            if (entry.Name.HasValue && long.TryParse((string?)entry.Value, out var value))
            {
                map[entry.Name!] = value;
            }
        }

        return map;
    }

    private static long GetField(IReadOnlyDictionary<string, long> fields, string key) =>
        fields.TryGetValue(key, out var value) ? value : 0;
}
