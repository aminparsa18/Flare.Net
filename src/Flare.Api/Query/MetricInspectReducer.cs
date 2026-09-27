using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>One raw data point as read by <see cref="MetricInspectQueryBuilder.BuildSamples"/>.</summary>
public sealed record MetricInspectRawSample(long TimeUnixMs, double Value, bool IsDelta, bool IsMonotonic);

/// <summary>One series' raw samples, oldest first.</summary>
public sealed record MetricInspectRawSeries(
    string ServiceName,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<MetricInspectRawSample> Samples,
    bool SamplesTruncated);

/// <summary>What <see cref="MetricInspectReducer.Reduce"/> returns: the annotated series and their merge.</summary>
public sealed record MetricInspectReduction(IReadOnlyList<MetricInspectSeries> Series, IReadOnlyList<MetricInspectBucket> Merged);

/// <summary>
/// The "inspect metric" view's two reduction steps over raw samples, in the same terms the
/// Metrics explorer's SQL uses (<see cref="MetricSeriesQueryBuilder"/>): step 1 (time
/// aggregation) turns each series' samples into one value per bucket; step 2 (space
/// aggregation) merges the series per bucket, as a grouped chart merges the series sharing a
/// group-by value.
/// </summary>
/// <remarks>
/// <para>
/// Gauge: bucket = <c>avg(Value)</c>; merge = the average of every sample in the bucket across
/// the series, which is what <c>avg(Value)</c> over a wider group computes - not the mean of
/// the per-series averages.
/// </para>
/// <para>
/// Sum (and a histogram's <c>Count</c>): each sample is classified exactly as the Sum query's
/// <c>multiIf</c> does (ADR-0035) - delta as-is, a series' first sample 0, a non-monotonic
/// difference as-is, a monotonic drop taken as a reset (the value itself), otherwise the
/// difference - and a bucket is the sum of its samples' contributions; merge = the sum of the
/// series' buckets. A series cut to its newest samples starts its differencing later than the
/// explorer would, which only changes the first shown sample's contribution.
/// </para>
/// <para>
/// Kept in lockstep with <see cref="MetricSeriesQueryBuilder"/> by hand: if that query's
/// per-type reduction changes, this has to change with it.
/// </para>
/// </remarks>
public static class MetricInspectReducer
{
    public static MetricInspectReduction Reduce(MetricPointType type, IReadOnlyList<MetricInspectRawSeries> series, int bucketWidthSeconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bucketWidthSeconds);
        var bucketWidthMs = bucketWidthSeconds * 1000L;
        var isGauge = type == MetricPointType.Gauge;

        // Per bucket start: sum of values (gauge) or contributions, and sample count - across
        // every series, for the merge step.
        var merged = new SortedDictionary<long, (double Total, int Count)>();
        var result = new List<MetricInspectSeries>(series.Count);

        foreach (var raw in series)
        {
            var samples = Classify(raw.Samples, isGauge);
            var buckets = new List<MetricInspectBucket>();

            var i = 0;
            while (i < samples.Count)
            {
                var start = BucketStart(samples[i].TimeUnixMs, bucketWidthMs);
                double total = 0;
                var count = 0;
                for (; i < samples.Count && BucketStart(samples[i].TimeUnixMs, bucketWidthMs) == start; i++)
                {
                    total += samples[i].Contribution;
                    count++;
                }

                buckets.Add(new MetricInspectBucket
                {
                    BucketStartUnixMs = start,
                    Value = isGauge ? total / count : total,
                    SampleCount = count,
                });

                merged[start] = merged.TryGetValue(start, out var m) ? (m.Total + total, m.Count + count) : (total, count);
            }

            result.Add(new MetricInspectSeries
            {
                ServiceName = raw.ServiceName,
                Attributes = raw.Attributes,
                Samples = samples,
                SamplesTruncated = raw.SamplesTruncated,
                Buckets = buckets,
            });
        }

        var mergedBuckets = merged
            .Select(kv => new MetricInspectBucket
            {
                BucketStartUnixMs = kv.Key,
                Value = isGauge ? kv.Value.Total / kv.Value.Count : kv.Value.Total,
                SampleCount = kv.Value.Count,
            })
            .ToList();

        return new MetricInspectReduction(result, mergedBuckets);
    }

    private static List<MetricInspectSample> Classify(IReadOnlyList<MetricInspectRawSample> samples, bool isGauge)
    {
        var result = new List<MetricInspectSample>(samples.Count);
        for (var i = 0; i < samples.Count; i++)
        {
            var s = samples[i];
            var (kind, contribution) = isGauge
                ? (MetricInspectSampleKind.Level, s.Value)
                : ClassifyCounter(s, i == 0 ? null : samples[i - 1]);
            result.Add(new MetricInspectSample { TimeUnixMs = s.TimeUnixMs, Value = s.Value, Kind = kind, Contribution = contribution });
        }

        return result;
    }

    /// <summary>The Sum query's <c>multiIf</c>, row for row - see this class's remarks.</summary>
    private static (MetricInspectSampleKind Kind, double Contribution) ClassifyCounter(MetricInspectRawSample sample, MetricInspectRawSample? previous)
    {
        if (sample.IsDelta)
        {
            return (MetricInspectSampleKind.Delta, sample.Value);
        }

        if (previous is null)
        {
            return (MetricInspectSampleKind.First, 0);
        }

        var difference = sample.Value - previous.Value;
        return sample.IsMonotonic && difference < 0
            ? (MetricInspectSampleKind.Reset, sample.Value)
            : (MetricInspectSampleKind.Difference, difference);
    }

    /// <summary>Same epoch-aligned floor as ClickHouse's <c>toStartOfInterval(Time, INTERVAL n SECOND)</c>.</summary>
    private static long BucketStart(long timeUnixMs, long bucketWidthMs) => timeUnixMs - (((timeUnixMs % bucketWidthMs) + bucketWidthMs) % bucketWidthMs);
}
