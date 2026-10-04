using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>One <see cref="RuntimeHealthQueryBuilder"/> row: a metric's value for one instance in one time bucket.</summary>
public sealed record RuntimeMetricPoint(DateTimeOffset Bucket, string Metric, string Instance, double Value);

/// <summary>
/// Pure detectors over the bucketed <c>dotnet.*</c> series <see cref="RuntimeHealthQueryBuilder"/>
/// returns - no ClickHouse, so every rule is unit-tested directly. Each rule is evaluated per
/// instance and reports one finding per run of consecutive buckets that break it.
/// </summary>
/// <remarks>
/// <para>The rules, with the thresholds as constants rather than settings (a fixed, documented
/// rule is easier to trust than a tunable one; make them configurable when someone needs to):</para>
/// <list type="bullet">
/// <item><b>Thread-pool starvation</b>: <see cref="StarvationMinQueue"/> or more queued items
/// while completed work items per second sit at or under half the instance's median, for
/// <see cref="StarvationMinBuckets"/> or more buckets in a row, with the queue no shorter at the
/// end of the run than at the start (a backlog that is draining isn't starvation).</item>
/// <item><b>GC pressure</b>: at least <see cref="GcMinFraction"/> of wall-clock time paused in GC
/// for <see cref="GcMinBuckets"/> or more buckets in a row.</item>
/// <item><b>Lock contention</b> and <b>exception rate</b>: events per second at least a multiple
/// of the instance's baseline <em>and</em> over an absolute floor, for
/// <see cref="JumpMinBuckets"/> or more buckets in a row. The baseline is the lower quartile of
/// the window's per-bucket rates, so a spike (or a step lasting up to three quarters of the
/// window) stands out against it.</item>
/// </list>
/// <para>Counter series drop their first bucket (the builder counts a series' first row as
/// zero), buckets cut short by the window edges are rated over their real length, and the
/// baseline rules need <see cref="MinBaselineBuckets"/> buckets of data before they fire.
/// A gap in the data ends a run.</para>
/// </remarks>
public static class RuntimeHealthDetector
{
    public const double StarvationMinQueue = 10;
    public const int StarvationMinBuckets = 3;
    public const double StarvationCriticalQueue = 100;

    public const double GcMinFraction = 0.10;
    public const int GcMinBuckets = 2;
    public const double GcCriticalFraction = 0.25;

    public const int JumpMinBuckets = 2;
    public const int MinBaselineBuckets = 6;

    public const double LockMultiple = 5;
    public const double LockFloorPerSecond = 5;
    public const double LockCriticalPerSecond = 50;

    public const double ExceptionMultiple = 3;
    public const double ExceptionFloorPerSecond = 2;
    public const double ExceptionCriticalPerSecond = 20;

    /// <summary>Both rate kinds are critical once ongoing and this many times their baseline (and over the kind's critical rate).</summary>
    public const double CriticalBaselineMultiple = 10;

    public const int MaxFindings = 50;

    /// <summary>Buckets shorter than this (cut by a window edge) are too noisy to rate.</summary>
    private const double MinBucketSeconds = 10;

    private readonly record struct Slot(DateTimeOffset Start, double Seconds, double Value)
    {
        public double Rate => Value / Seconds;
    }

    public static IReadOnlyList<RuntimeHealthFinding> Detect(
        IReadOnlyList<RuntimeMetricPoint> points,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        int bucketWidthSeconds)
    {
        var width = TimeSpan.FromSeconds(bucketWidthSeconds);
        var findings = new List<RuntimeHealthFinding>();

        foreach (var instance in points.GroupBy(p => p.Instance))
        {
            List<Slot> Slots(string metric, bool isCounter)
            {
                var slots = instance
                    .Where(p => p.Metric == metric)
                    .OrderBy(p => p.Bucket)
                    .Select(p =>
                    {
                        var from = p.Bucket > windowStart ? p.Bucket : windowStart;
                        var to = p.Bucket + width < windowEnd ? p.Bucket + width : windowEnd;
                        return new Slot(p.Bucket, (to - from).TotalSeconds, p.Value);
                    })
                    .ToList();
                // A counter's first bucket only holds the zero the builder assigns a series' first row.
                if (isCounter && slots.Count > 0) slots.RemoveAt(0);
                slots.RemoveAll(s => s.Seconds < MinBucketSeconds);
                return slots;
            }

            var queue = Slots(RuntimeHealthQueryBuilder.QueueLengthMetric, isCounter: false);
            var workItems = Slots(RuntimeHealthQueryBuilder.WorkItemsMetric, isCounter: true);
            var gcPause = Slots(RuntimeHealthQueryBuilder.GcPauseMetric, isCounter: true);
            var locks = Slots(RuntimeHealthQueryBuilder.LockContentionsMetric, isCounter: true);
            var exceptions = Slots(RuntimeHealthQueryBuilder.ExceptionsMetric, isCounter: true);

            DetectStarvation(instance.Key, queue, workItems, width, windowStart, windowEnd, findings);
            DetectGc(instance.Key, gcPause, width, windowStart, windowEnd, findings);
            DetectJump(RuntimeFindingKind.LockContention, instance.Key, locks, LockMultiple, LockFloorPerSecond, LockCriticalPerSecond, width, windowStart, windowEnd, findings);
            DetectJump(RuntimeFindingKind.ExceptionRate, instance.Key, exceptions, ExceptionMultiple, ExceptionFloorPerSecond, ExceptionCriticalPerSecond, width, windowStart, windowEnd, findings);
        }

        return findings
            .OrderByDescending(f => f.Severity)
            .ThenByDescending(f => f.StartUnixMs)
            .Take(MaxFindings)
            .ToList();
    }

    private static void DetectStarvation(
        string instance, List<Slot> queue, List<Slot> workItems, TimeSpan width,
        DateTimeOffset windowStart, DateTimeOffset windowEnd, List<RuntimeHealthFinding> findings)
    {
        if (queue.Count == 0 || workItems.Count == 0) return;

        var rateByBucket = workItems.ToDictionary(s => s.Start, s => s.Rate);
        double? baseline = workItems.Count >= MinBaselineBuckets ? Median(workItems.Select(s => s.Rate).ToList()) : null;
        var threshold = 0.5 * (baseline ?? 0);

        foreach (var run in Runs(queue, width, s => s.Value >= StarvationMinQueue && rateByBucket.TryGetValue(s.Start, out var rate) && rate <= threshold))
        {
            if (run.Count < StarvationMinBuckets || run[^1].Value < run[0].Value) continue;

            var peak = run.Max(s => s.Value);
            var ongoing = IsOngoing(run, width, windowEnd);
            findings.Add(Finding(
                RuntimeFindingKind.ThreadPoolStarvation, instance, run, width, windowStart, windowEnd, peak, baseline,
                ongoing && peak >= StarvationCriticalQueue));
        }
    }

    private static void DetectGc(
        string instance, List<Slot> gcPause, TimeSpan width,
        DateTimeOffset windowStart, DateTimeOffset windowEnd, List<RuntimeHealthFinding> findings)
    {
        foreach (var run in Runs(gcPause, width, s => s.Rate >= GcMinFraction))
        {
            if (run.Count < GcMinBuckets) continue;

            var peak = run.Max(s => s.Rate);
            findings.Add(Finding(
                RuntimeFindingKind.GcPressure, instance, run, width, windowStart, windowEnd, peak, baseline: null,
                IsOngoing(run, width, windowEnd) && peak >= GcCriticalFraction));
        }
    }

    private static void DetectJump(
        RuntimeFindingKind kind, string instance, List<Slot> slots, double multiple, double floor, double criticalFloor,
        TimeSpan width, DateTimeOffset windowStart, DateTimeOffset windowEnd, List<RuntimeHealthFinding> findings)
    {
        if (slots.Count < MinBaselineBuckets) return;

        var rates = slots.Select(s => s.Rate).OrderBy(r => r).ToList();
        var baseline = rates[(rates.Count - 1) / 4];
        var trigger = Math.Max(multiple * baseline, floor);

        foreach (var run in Runs(slots, width, s => s.Rate >= trigger))
        {
            if (run.Count < JumpMinBuckets) continue;

            var peak = run.Max(s => s.Rate);
            findings.Add(Finding(
                kind, instance, run, width, windowStart, windowEnd, peak, baseline,
                IsOngoing(run, width, windowEnd) && peak >= criticalFloor && peak >= CriticalBaselineMultiple * baseline));
        }
    }

    private static RuntimeHealthFinding Finding(
        RuntimeFindingKind kind, string instance, List<Slot> run, TimeSpan width,
        DateTimeOffset windowStart, DateTimeOffset windowEnd, double value, double? baseline, bool critical)
    {
        var start = run[0].Start > windowStart ? run[0].Start : windowStart;
        var end = run[^1].Start + width < windowEnd ? run[^1].Start + width : windowEnd;
        return new RuntimeHealthFinding
        {
            Kind = kind,
            Severity = critical ? RuntimeFindingSeverity.Critical : RuntimeFindingSeverity.Warning,
            Instance = instance,
            StartUnixMs = start.ToUnixTimeMilliseconds(),
            EndUnixMs = end.ToUnixTimeMilliseconds(),
            Ongoing = IsOngoing(run, width, windowEnd),
            Value = value,
            Baseline = baseline,
        };
    }

    /// <summary>The run reaches one of the last two buckets - the newest bucket is often partial, so touching it alone is too strict.</summary>
    private static bool IsOngoing(List<Slot> run, TimeSpan width, DateTimeOffset windowEnd) =>
        run[^1].Start + width >= windowEnd - width;

    /// <summary>Maximal runs of adjacent buckets (exactly one width apart) satisfying <paramref name="predicate"/>.</summary>
    private static List<List<Slot>> Runs(List<Slot> slots, TimeSpan width, Func<Slot, bool> predicate)
    {
        var runs = new List<List<Slot>>();
        List<Slot>? current = null;
        foreach (var slot in slots)
        {
            if (!predicate(slot))
            {
                current = null;
                continue;
            }

            if (current is not null && slot.Start - current[^1].Start == width)
            {
                current.Add(slot);
            }
            else
            {
                current = [slot];
                runs.Add(current);
            }
        }

        return runs;
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        var mid = values.Count / 2;
        return values.Count % 2 == 1 ? values[mid] : (values[mid - 1] + values[mid]) / 2;
    }
}
