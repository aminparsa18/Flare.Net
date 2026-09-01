using System.Globalization;
using Flare.Ingest.Model;
using Flare.Ingest.Otlp;

namespace Flare.Ingest.Prometheus;

/// <summary>
/// Maps a parsed <see cref="PrometheusParseResult"/> plus one scrape target's identity into
/// Flare's internal <see cref="MetricPointRecord"/> model - the Prometheus-side counterpart
/// to <see cref="OtlpMetricsMapper"/>, returning the same <see cref="MetricMapResult"/> type
/// (reused rather than duplicated - both mappers feed the one metrics pipeline downstream).
/// </summary>
/// <remarks>
/// Resource attribution follows the OTel Collector's own <c>prometheusreceiver</c>
/// convention, not a novel mapping: <c>service.name</c> = the target's configured
/// <see cref="PrometheusScrapeTargetOptions.Job"/>, <c>service.instance.id</c> = the
/// target URL's authority (host:port) - matching Prometheus's own <c>instance</c> label,
/// which is always in that form. <see cref="PrometheusScrapeTargetOptions.Labels"/> is
/// merged in last, so a target can override either.
///
/// counter -&gt; <see cref="SumPointRecord"/> (always monotonic, always cumulative -
/// Prometheus counters only ever count up since process start, there's no delta flavor on
/// the wire). gauge/untyped -&gt; <see cref="GaugePointRecord"/> (untyped defaults to gauge
/// per standard Prometheus consumer convention - <see cref="PrometheusExpositionParser"/>
/// itself makes no such assumption, this mapper does). histogram -&gt;
/// <see cref="HistogramPointRecord"/>, converting Prometheus's cumulative
/// <c>_bucket{le=...}</c> counts into OTLP's non-cumulative <see cref="HistogramPointRecord.BucketCounts"/>.
/// summary is dropped (same "not in v1 scope" treatment <see cref="OtlpMetricsMapper"/>
/// already gives ExponentialHistogram/Summary on the OTLP side).
///
/// <c>scrapeTime</c> (see <see cref="Map"/> below) does double duty here in a way it
/// doesn't for the OTLP mappers: it's both the fallback <c>Time</c> for a sample with no
/// embedded timestamp of its own (unchanged from before ADR-0014) *and* the value every
/// mapped point's <see cref="MetricPointRecord.IngestedAt"/> is stamped with - because
/// for a pull-based scrape, "when did Flare receive this" and "what time did Flare's own
/// clock read when it initiated the scrape" are the same moment (<c>PrometheusScrapeWorker</c>
/// passes <c>timeProvider.GetUtcNow()</c> straight through), unlike OTLP's push model
/// where the client's own clock stamps the event time first and Flare only sees it later.
/// </remarks>
public static class PrometheusMetricsMapper
{
    /// <param name="parsed">The parsed Prometheus exposition-format samples.</param>
    /// <param name="target">The scrape target's own identity/label configuration.</param>
    /// <param name="scrapeTime"><c>Flare.Ingest</c>'s own wall-clock read at the moment this scrape was initiated - see this type's remarks for why it also becomes every point's <see cref="MetricPointRecord.IngestedAt"/>.</param>
    public static MetricMapResult Map(PrometheusParseResult parsed, PrometheusScrapeTargetOptions target, DateTimeOffset scrapeTime)
    {
        var resourceAttributes = BuildResourceAttributes(target);
        var serviceName = resourceAttributes.GetValueOrDefault("service.name");

        var points = new List<MetricPointRecord>();
        var unsupported = new HashSet<string>(StringComparer.Ordinal);
        var consumed = new bool[parsed.Samples.Count];

        // Histogram/summary base names are declared via # TYPE - handle those sample
        // families (by suffix) first so the plain loop below doesn't also re-map their
        // _bucket/_sum/_count lines as standalone gauges.
        foreach (var (baseName, type) in parsed.Types)
        {
            if (type == PrometheusMetricType.Histogram)
            {
                MapHistogram(parsed, baseName, consumed, resourceAttributes, serviceName, scrapeTime, points);
            }
            else if (type == PrometheusMetricType.Summary)
            {
                MarkSummaryConsumed(parsed, baseName, consumed);
                unsupported.Add(baseName);
            }
        }

        for (var i = 0; i < parsed.Samples.Count; i++)
        {
            if (consumed[i])
            {
                continue;
            }

            var sample = parsed.Samples[i];
            var type = parsed.Types.GetValueOrDefault(sample.Name, PrometheusMetricType.Untyped);
            var time = sample.TimestampMillis is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : scrapeTime;
            var description = parsed.HelpText.GetValueOrDefault(sample.Name);

            switch (type)
            {
                case PrometheusMetricType.Counter:
                    points.Add(new SumPointRecord
                    {
                        MetricName = sample.Name,
                        Description = description,
                        Unit = null,
                        ServiceName = serviceName,
                        ResourceSchemaUrl = null,
                        ResourceAttributes = resourceAttributes,
                        ScopeSchemaUrl = null,
                        ScopeName = null,
                        ScopeVersion = null,
                        ScopeAttributes = EmptyAttributes,
                        DataPointAttributes = sample.Labels,
                        StartTime = null,
                        Time = time,
                        IngestedAt = scrapeTime,
                        Value = sample.Value,
                        AggregationTemporality = CumulativeTemporality,
                        IsMonotonic = true,
                    });
                    break;

                case PrometheusMetricType.Gauge:
                case PrometheusMetricType.Untyped:
                    points.Add(new GaugePointRecord
                    {
                        MetricName = sample.Name,
                        Description = description,
                        Unit = null,
                        ServiceName = serviceName,
                        ResourceSchemaUrl = null,
                        ResourceAttributes = resourceAttributes,
                        ScopeSchemaUrl = null,
                        ScopeName = null,
                        ScopeVersion = null,
                        ScopeAttributes = EmptyAttributes,
                        DataPointAttributes = sample.Labels,
                        StartTime = null,
                        Time = time,
                        IngestedAt = scrapeTime,
                        Value = sample.Value,
                    });
                    break;

                default:
                    // Histogram/Summary declared but this sample didn't fit the expected
                    // _bucket/_sum/_count shape (e.g. a bare sample under a histogram
                    // TYPE line) - drop rather than silently mis-map as a gauge.
                    unsupported.Add(sample.Name);
                    break;
            }
        }

        return new MetricMapResult(points, [.. unsupported]);
    }

    /// <summary>OTLP AggregationTemporality.AGGREGATION_TEMPORALITY_CUMULATIVE (metrics.proto) - Prometheus counters/histograms are always cumulative since process start.</summary>
    private const int CumulativeTemporality = 2;

    private static readonly Dictionary<string, string> EmptyAttributes = [];

    private static Dictionary<string, string> BuildResourceAttributes(PrometheusScrapeTargetOptions target)
    {
        var attrs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["service.name"] = target.Job,
        };

        if (Uri.TryCreate(target.Url, UriKind.Absolute, out var uri))
        {
            attrs["service.instance.id"] = $"{uri.Host}:{uri.Port}";
        }

        foreach (var (key, value) in target.Labels)
        {
            attrs[key] = value;
        }

        return attrs;
    }

    private sealed class HistogramGroup
    {
        public required IReadOnlyDictionary<string, string> Labels { get; init; }
        public List<(double Le, double Cumulative)> Buckets { get; } = [];
        public double? Count { get; set; }
        public double? Sum { get; set; }
        public long? TimestampMillis { get; set; }
    }

    private static void MapHistogram(
        PrometheusParseResult parsed,
        string baseName,
        bool[] consumed,
        IReadOnlyDictionary<string, string> resourceAttributes,
        string? serviceName,
        DateTimeOffset scrapeTime,
        List<MetricPointRecord> points)
    {
        var bucketSuffix = baseName + "_bucket";
        var sumSuffix = baseName + "_sum";
        var countSuffix = baseName + "_count";

        var groups = new Dictionary<string, HistogramGroup>(StringComparer.Ordinal);

        for (var i = 0; i < parsed.Samples.Count; i++)
        {
            var sample = parsed.Samples[i];

            if (sample.Name == bucketSuffix)
            {
                if (!sample.Labels.TryGetValue("le", out var leText) || !TryParseNumber(leText, out var le))
                {
                    continue; // malformed bucket line - not part of a valid histogram, leave it for the plain loop to drop
                }

                var groupLabels = WithoutKey(sample.Labels, "le");
                var group = GetOrAddGroup(groups, groupLabels);
                group.Buckets.Add((le, sample.Value));
                group.TimestampMillis ??= sample.TimestampMillis;
                consumed[i] = true;
            }
            else if (sample.Name == sumSuffix)
            {
                var group = GetOrAddGroup(groups, sample.Labels);
                group.Sum = sample.Value;
                group.TimestampMillis ??= sample.TimestampMillis;
                consumed[i] = true;
            }
            else if (sample.Name == countSuffix)
            {
                var group = GetOrAddGroup(groups, sample.Labels);
                group.Count = sample.Value;
                group.TimestampMillis ??= sample.TimestampMillis;
                consumed[i] = true;
            }
        }

        var description = parsed.HelpText.GetValueOrDefault(baseName);

        foreach (var group in groups.Values)
        {
            group.Buckets.Sort((a, b) => a.Le.CompareTo(b.Le));

            var explicitBounds = new List<double>(group.Buckets.Count);
            var bucketCounts = new List<ulong>(group.Buckets.Count);
            var previousCumulative = 0d;

            foreach (var (le, cumulative) in group.Buckets)
            {
                if (!double.IsPositiveInfinity(le))
                {
                    explicitBounds.Add(le);
                }

                var delta = cumulative - previousCumulative;
                bucketCounts.Add(delta > 0 ? (ulong)Math.Round(delta) : 0UL);
                previousCumulative = cumulative;
            }

            var count = group.Count
                ?? (group.Buckets.Count > 0 ? group.Buckets[^1].Cumulative : 0d);

            var time = group.TimestampMillis is { } ms ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : scrapeTime;

            points.Add(new HistogramPointRecord
            {
                MetricName = baseName,
                Description = description,
                Unit = null,
                ServiceName = serviceName,
                ResourceSchemaUrl = null,
                ResourceAttributes = resourceAttributes,
                ScopeSchemaUrl = null,
                ScopeName = null,
                ScopeVersion = null,
                ScopeAttributes = EmptyAttributes,
                DataPointAttributes = group.Labels,
                StartTime = null,
                Time = time,
                IngestedAt = scrapeTime,
                AggregationTemporality = CumulativeTemporality,
                Count = count < 0 ? 0UL : (ulong)Math.Round(count),
                Sum = group.Sum,
                BucketCounts = bucketCounts,
                ExplicitBounds = explicitBounds,
            });
        }
    }

    private static void MarkSummaryConsumed(PrometheusParseResult parsed, string baseName, bool[] consumed)
    {
        var sumSuffix = baseName + "_sum";
        var countSuffix = baseName + "_count";

        for (var i = 0; i < parsed.Samples.Count; i++)
        {
            var name = parsed.Samples[i].Name;
            if (name == baseName || name == sumSuffix || name == countSuffix)
            {
                consumed[i] = true;
            }
        }
    }

    private static HistogramGroup GetOrAddGroup(Dictionary<string, HistogramGroup> groups, IReadOnlyDictionary<string, string> labels)
    {
        var key = LabelKey(labels);
        if (!groups.TryGetValue(key, out var group))
        {
            group = new HistogramGroup { Labels = labels };
            groups[key] = group;
        }

        return group;
    }

    private static string LabelKey(IReadOnlyDictionary<string, string> labels) =>
        string.Join('\u0001', labels.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));

    private static Dictionary<string, string> WithoutKey(IReadOnlyDictionary<string, string> labels, string key)
    {
        var result = new Dictionary<string, string>(labels.Count, StringComparer.Ordinal);
        foreach (var (k, v) in labels)
        {
            if (!string.Equals(k, key, StringComparison.Ordinal))
            {
                result[k] = v;
            }
        }

        return result;
    }

    private static bool TryParseNumber(string token, out double value)
    {
        switch (token)
        {
            case "+Inf":
            case "Inf":
                value = double.PositiveInfinity;
                return true;
            case "-Inf":
                value = double.NegativeInfinity;
                return true;
            case "NaN":
                value = double.NaN;
                return true;
            default:
                return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
