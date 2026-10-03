using System.Text;
using Flare.Ingest.Model;

namespace Flare.Ingest.Pipeline.MetricRules;

/// <summary>
/// Pure: applies <see cref="MetricAttributeRule"/>s to a flush batch's data points and
/// re-aggregates the points that end up on the same series at the same timestamp.
/// See docs-internal/adr/0083-metric-attribute-reduction.md.
/// </summary>
/// <remarks>
/// Only data-point attributes are reduced - resource/scope attributes identify the emitter,
/// not the measurement. Points no rule touched pass through untouched and are never merged.
/// Merging is scoped to one batch, so duplicates that straddle batches stay separate rows
/// (harmless for delta data, which queries sum). Merge rules for points that collapse onto
/// the same key: Gauge and cumulative Sum/Histogram keep the last value; delta Sum adds;
/// delta Histogram adds counts/sum/buckets when the bounds match (different bounds never
/// share a key). ExponentialHistogram points are reduced but never merged - aligning
/// scales/offsets across points isn't worth the risk, and their rows sum at query time anyway.
/// </remarks>
public static class MetricAttributeReducer
{
    private const int DeltaTemporality = 1;

    public static IReadOnlyList<MetricPointRecord> Reduce(IReadOnlyList<MetricPointRecord> points, IReadOnlyList<MetricAttributeRule> rules)
    {
        if (rules.Count == 0 || points.Count == 0)
        {
            return points;
        }

        var result = new List<MetricPointRecord>(points.Count);
        var merged = new Dictionary<string, int>();
        var anyReduced = false;
        var exponentialSeq = 0;

        foreach (var point in points)
        {
            var reduced = ReduceAttributes(point, rules);
            if (ReferenceEquals(reduced, point))
            {
                result.Add(point);
                continue;
            }

            anyReduced = true;
            var key = reduced is ExponentialHistogramPointRecord ? $"exp:{exponentialSeq++}" : SeriesKey(reduced);
            if (merged.TryGetValue(key, out var index))
            {
                result[index] = Merge(result[index], reduced);
            }
            else
            {
                merged[key] = result.Count;
                result.Add(reduced);
            }
        }

        return anyReduced ? result : points;
    }

    public static bool Matches(MetricAttributeRule rule, string metricName) =>
        rule.MetricName.EndsWith('*')
            ? metricName.StartsWith(rule.MetricName.AsSpan(0, rule.MetricName.Length - 1), StringComparison.Ordinal)
            : string.Equals(rule.MetricName, metricName, StringComparison.Ordinal);

    private static MetricPointRecord ReduceAttributes(MetricPointRecord point, IReadOnlyList<MetricAttributeRule> rules)
    {
        var attributes = point.DataPointAttributes;
        foreach (var rule in rules)
        {
            if (rule.Attributes.Count == 0 || !Matches(rule, point.MetricName))
            {
                continue;
            }

            var drop = rule.Mode == MetricAttributeRuleMode.Drop;
            var filtered = attributes.Where(kv => rule.Attributes.Contains(kv.Key) != drop).ToDictionary(kv => kv.Key, kv => kv.Value);
            if (filtered.Count != attributes.Count)
            {
                attributes = filtered;
            }
        }

        return ReferenceEquals(attributes, point.DataPointAttributes) ? point : point with { DataPointAttributes = attributes };
    }

    private static string SeriesKey(MetricPointRecord p)
    {
        var sb = new StringBuilder();
        sb.Append(p.GetType().Name).Append('\u001f').Append(p.MetricName).Append('\u001f').Append(p.ServiceName)
            .Append('\u001f').Append(p.ResourceSchemaUrl).Append('\u001f').Append(p.ScopeName).Append('\u001f').Append(p.ScopeVersion)
            .Append('\u001f').Append(p.ScopeSchemaUrl).Append('\u001f').Append(p.Time.UtcTicks)
            .Append('\u001f').Append(p.StartTime?.UtcTicks);
        AppendAttributes(sb, p.ResourceAttributes);
        AppendAttributes(sb, p.ScopeAttributes);
        AppendAttributes(sb, p.DataPointAttributes);
        switch (p)
        {
            case SumPointRecord s:
                sb.Append('\u001f').Append(s.AggregationTemporality).Append(s.IsMonotonic);
                break;
            case HistogramPointRecord h:
                sb.Append('\u001f').Append(h.AggregationTemporality).Append('\u001f').AppendJoin(',', h.ExplicitBounds);
                break;
        }

        return sb.ToString();
    }

    private static void AppendAttributes(StringBuilder sb, IReadOnlyDictionary<string, string> attributes)
    {
        sb.Append('\u001e');
        foreach (var (k, v) in attributes.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            sb.Append(k).Append('\u001d').Append(v).Append('\u001c');
        }
    }

    private static MetricPointRecord Merge(MetricPointRecord existing, MetricPointRecord incoming) => (existing, incoming) switch
    {
        (SumPointRecord a, SumPointRecord b) when a.AggregationTemporality == DeltaTemporality => a with { Value = a.Value + b.Value },
        (HistogramPointRecord a, HistogramPointRecord b) when a.AggregationTemporality == DeltaTemporality && a.BucketCounts.Count == b.BucketCounts.Count => a with
        {
            Count = a.Count + b.Count,
            Sum = a.Sum is null && b.Sum is null ? null : (a.Sum ?? 0) + (b.Sum ?? 0),
            BucketCounts = a.BucketCounts.Zip(b.BucketCounts, (x, y) => x + y).ToArray(),
        },
        _ => incoming with { IngestedAt = existing.IngestedAt },
    };
}
