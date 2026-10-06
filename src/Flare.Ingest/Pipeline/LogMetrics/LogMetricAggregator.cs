using Flare.Ingest.Model;
using Flare.Ingest.Pipeline.Rules;

namespace Flare.Ingest.Pipeline.LogMetrics;

/// <summary>
/// Pure log batch → metric points aggregation: for every definition, counts the events its
/// condition matches, per (service, time bucket, group-by values), and returns one delta
/// monotonic <see cref="SumPointRecord"/> per group. No I/O, same "pure function, unit-testable
/// on its own" style as <see cref="MetricRules.MetricAttributeReducer"/>.
/// </summary>
/// <remarks>
/// Counts are per flush batch, so one bucket straddling two batches becomes two rows; both are
/// delta sums, which queries add up, so that is harmless. Resource attributes are deliberately
/// not carried onto the point (only <c>service.name</c>, via <see cref="MetricPointRecord.ServiceName"/>):
/// they would multiply the series count by every pod and host that logs.
/// </remarks>
public static class LogMetricAggregator
{
    /// <summary>Value every group-by attribute takes once a definition has hit <see cref="LogMetricOptions.MaxGroupsPerDefinition"/>.</summary>
    public const string OverflowValue = "__overflow__";

    public const string ScopeName = "flare.log-metrics";

    private const int DeltaTemporality = 1;

    public static IReadOnlyList<SumPointRecord> Aggregate(
        IReadOnlyList<LogEvent> events,
        IReadOnlyList<LogMetricDefinition> definitions,
        LogMetricOptions options,
        DateTimeOffset now)
    {
        if (events.Count == 0 || definitions.Count == 0)
        {
            return [];
        }

        var bucketTicks = Math.Max(TimeSpan.TicksPerSecond, options.BucketSize.Ticks);
        var points = new List<SumPointRecord>();

        foreach (var definition in definitions)
        {
            var counts = new Dictionary<GroupKey, long>();
            var seenGroups = new HashSet<string[]>(StringArrayComparer.Instance);

            foreach (var logEvent in events)
            {
                if (!PipelineRuleConditionMatcher.Matches(logEvent, definition.Condition))
                {
                    continue;
                }

                var values = ResolveGroupValues(logEvent, definition.GroupBy);
                if (!seenGroups.Contains(values))
                {
                    if (seenGroups.Count >= options.MaxGroupsPerDefinition)
                    {
                        values = [.. definition.GroupBy.Select(_ => OverflowValue)];
                    }

                    seenGroups.Add(values);
                }

                var bucket = logEvent.Timestamp.UtcTicks / bucketTicks * bucketTicks;
                var key = new GroupKey(logEvent.ServiceName ?? "", bucket, values);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }

            foreach (var (key, count) in counts)
            {
                var time = new DateTimeOffset(key.BucketTicks, TimeSpan.Zero);
                var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
                for (var i = 0; i < definition.GroupBy.Count; i++)
                {
                    if (key.Values[i].Length > 0)
                    {
                        attributes[definition.GroupBy[i]] = key.Values[i];
                    }
                }

                points.Add(new SumPointRecord
                {
                    MetricName = definition.MetricName,
                    Unit = "{log}",
                    ServiceName = key.Service.Length == 0 ? null : key.Service,
                    ResourceAttributes = new Dictionary<string, string>(),
                    ScopeName = ScopeName,
                    ScopeAttributes = new Dictionary<string, string>(),
                    DataPointAttributes = attributes,
                    StartTime = time,
                    Time = time,
                    IngestedAt = now,
                    Value = count,
                    AggregationTemporality = DeltaTemporality,
                    IsMonotonic = true,
                });
            }
        }

        return points;
    }

    /// <summary>One value per group-by key, in key order; "" when the log carries neither a log nor a resource attribute of that name.</summary>
    private static string[] ResolveGroupValues(LogEvent logEvent, IReadOnlyList<string> groupBy)
    {
        var values = new string[groupBy.Count];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = logEvent.LogAttributes.TryGetValue(groupBy[i], out var v) ? v
                : logEvent.ResourceAttributes.TryGetValue(groupBy[i], out v) ? v
                : "";
        }

        return values;
    }

    private readonly record struct GroupKey(string Service, long BucketTicks, string[] Values)
    {
        public bool Equals(GroupKey other) =>
            Service == other.Service && BucketTicks == other.BucketTicks && Values.AsSpan().SequenceEqual(other.Values);

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(Service);
            hash.Add(BucketTicks);
            foreach (var v in Values)
            {
                hash.Add(v);
            }

            return hash.ToHashCode();
        }
    }

    private sealed class StringArrayComparer : IEqualityComparer<string[]>
    {
        public static readonly StringArrayComparer Instance = new();

        public bool Equals(string[]? x, string[]? y) => x!.AsSpan().SequenceEqual(y);

        public int GetHashCode(string[] obj)
        {
            var hash = new HashCode();
            foreach (var v in obj)
            {
                hash.Add(v);
            }

            return hash.ToHashCode();
        }
    }
}
