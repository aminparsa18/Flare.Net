using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class RuntimeHealthDetectorTests
{
    private const int Width = 60;
    private static readonly DateTimeOffset Start = new(2026, 10, 3, 11, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddMinutes(60);

    private static DateTimeOffset BucketAt(int i) => Start.AddSeconds(i * Width);

    /// <summary>One point per bucket for 60 buckets; <paramref name="valueAt"/> returns null to leave a gap.</summary>
    private static IEnumerable<RuntimeMetricPoint> Series(string metric, Func<int, double?> valueAt, string instance = "pod-a") =>
        Enumerable.Range(0, 60)
            .Select(i => (i, v: valueAt(i)))
            .Where(t => t.v is not null)
            .Select(t => new RuntimeMetricPoint(BucketAt(t.i), metric, instance, t.v!.Value));

    private static IReadOnlyList<RuntimeHealthFinding> Detect(params IEnumerable<RuntimeMetricPoint>[] series) =>
        RuntimeHealthDetector.Detect(series.SelectMany(s => s).ToList(), Start, End, Width);

    // Work items: 100/s steady -> 6000 per 60s bucket.
    private static IEnumerable<RuntimeMetricPoint> Work(Func<int, double> perBucket, string instance = "pod-a") =>
        Series(RuntimeHealthQueryBuilder.WorkItemsMetric, i => perBucket(i), instance);

    private static IEnumerable<RuntimeMetricPoint> Queue(Func<int, double> length, string instance = "pod-a") =>
        Series(RuntimeHealthQueryBuilder.QueueLengthMetric, i => length(i), instance);

    [Fact]
    public void Starvation_QueueGrowsWhileCompletionsFlatline_Fires()
    {
        var findings = Detect(
            Queue(i => i is >= 40 and <= 45 ? 20 + (i - 40) * 30 : 0),
            Work(i => i is >= 40 and <= 45 ? 0 : 6000));

        var finding = Assert.Single(findings);
        Assert.Equal(RuntimeFindingKind.ThreadPoolStarvation, finding.Kind);
        Assert.Equal(170, finding.Value);
        Assert.Equal(100, finding.Baseline);
        Assert.Equal(BucketAt(40).ToUnixTimeMilliseconds(), finding.StartUnixMs);
        Assert.Equal(BucketAt(46).ToUnixTimeMilliseconds(), finding.EndUnixMs);
        Assert.False(finding.Ongoing);
        Assert.Equal(RuntimeFindingSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void Starvation_StillRunningAtWindowEndWithLargeQueue_IsCritical()
    {
        var findings = Detect(
            Queue(i => i >= 50 ? 100 + (i - 50) * 50 : 0),
            Work(i => i >= 50 ? 0 : 6000));

        var finding = Assert.Single(findings);
        Assert.True(finding.Ongoing);
        Assert.Equal(RuntimeFindingSeverity.Critical, finding.Severity);
    }

    [Fact]
    public void Starvation_QueueDrainingByEndOfRun_DoesNotFire()
    {
        var findings = Detect(
            Queue(i => i switch { 40 => 200, 41 => 150, 42 => 100, 43 => 50, _ => 0 }),
            Work(i => i is >= 40 and <= 43 ? 0 : 6000));

        Assert.Empty(findings);
    }

    [Fact]
    public void Starvation_QueueDeepButWorkContinues_DoesNotFire()
    {
        var findings = Detect(Queue(i => i >= 40 ? 500 : 0), Work(_ => 6000));

        Assert.Empty(findings);
    }

    [Fact]
    public void Starvation_TwoBucketsOnly_IsTooShort()
    {
        var findings = Detect(
            Queue(i => i is 40 or 41 ? 100 : 0),
            Work(i => i is 40 or 41 ? 0 : 6000));

        Assert.Empty(findings);
    }

    [Fact]
    public void Starvation_IsPerInstance_HealthyReplicaDoesNotHideStarvedOne()
    {
        var findings = Detect(
            Queue(i => i >= 40 ? 50 + i : 0, "pod-a"), Work(i => i >= 40 ? 0 : 6000, "pod-a"),
            Queue(_ => 0, "pod-b"), Work(_ => 6000, "pod-b"));

        var finding = Assert.Single(findings);
        Assert.Equal("pod-a", finding.Instance);
    }

    [Fact]
    public void GcPressure_SustainedTimeInGc_Fires()
    {
        // 9s of pause in a 60s bucket = 15%.
        var findings = Detect(Series(RuntimeHealthQueryBuilder.GcPauseMetric, i => i is >= 20 and <= 23 ? 9 : 0.5));

        var finding = Assert.Single(findings);
        Assert.Equal(RuntimeFindingKind.GcPressure, finding.Kind);
        Assert.Equal(0.15, finding.Value, 3);
        Assert.Null(finding.Baseline);
    }

    [Fact]
    public void GcPressure_SingleBucket_DoesNotFire()
    {
        var findings = Detect(Series(RuntimeHealthQueryBuilder.GcPauseMetric, i => i == 20 ? 30 : 0.5));

        Assert.Empty(findings);
    }

    [Fact]
    public void GcPressure_OngoingAndOverQuarter_IsCritical()
    {
        var findings = Detect(Series(RuntimeHealthQueryBuilder.GcPauseMetric, i => i >= 55 ? 24 : 0.5));

        Assert.Equal(RuntimeFindingSeverity.Critical, Assert.Single(findings).Severity);
    }

    [Fact]
    public void LockContention_JumpOverBaselineAndFloor_Fires()
    {
        // Baseline 1/s (60 per bucket); jump to 20/s (1200 per bucket) for three buckets.
        var findings = Detect(Series(RuntimeHealthQueryBuilder.LockContentionsMetric, i => i is >= 30 and <= 32 ? 1200 : 60));

        var finding = Assert.Single(findings);
        Assert.Equal(RuntimeFindingKind.LockContention, finding.Kind);
        Assert.Equal(20, finding.Value);
        Assert.Equal(1, finding.Baseline);
    }

    [Fact]
    public void LockContention_BigMultipleButUnderFloor_DoesNotFire()
    {
        // 0 -> 3/s is infinitely "bigger" than baseline, but under the 5/s floor.
        var findings = Detect(Series(RuntimeHealthQueryBuilder.LockContentionsMetric, i => i is >= 30 and <= 32 ? 180 : 0));

        Assert.Empty(findings);
    }

    [Fact]
    public void ExceptionRate_Jump_Fires_AndSteadyHighRateDoesNot()
    {
        var jump = Detect(Series(RuntimeHealthQueryBuilder.ExceptionsMetric, i => i is >= 10 and <= 12 ? 600 : 30));
        Assert.Equal(RuntimeFindingKind.ExceptionRate, Assert.Single(jump).Kind);

        var steady = Detect(Series(RuntimeHealthQueryBuilder.ExceptionsMetric, _ => 600));
        Assert.Empty(steady);
    }

    [Fact]
    public void Jump_NeedsEnoughBucketsForABaseline()
    {
        var points = Enumerable.Range(10, 4)
            .Select(i => new RuntimeMetricPoint(BucketAt(i), RuntimeHealthQueryBuilder.ExceptionsMetric, "pod-a", i >= 12 ? 6000 : 0))
            .ToList();

        Assert.Empty(RuntimeHealthDetector.Detect(points, Start, End, Width));
    }

    [Fact]
    public void Run_IsBrokenByAGapInTheData()
    {
        var findings = Detect(Series(RuntimeHealthQueryBuilder.GcPauseMetric, i => i switch { 20 => 9, 21 => null, 22 => 9, _ => 0.5 }));

        Assert.Empty(findings);
    }

    [Fact]
    public void CounterFirstBucket_IsIgnored()
    {
        // The builder reports 0 for a series' first row; as work items that must not read as "flatlined".
        var findings = Detect(
            Queue(i => i <= 3 ? 50 + i : 0),
            Work(i => i == 0 ? 0 : 6000));

        Assert.Empty(findings);
    }

    [Fact]
    public void Findings_AreOrderedCriticalFirstThenNewest()
    {
        var findings = Detect(
            Series(RuntimeHealthQueryBuilder.GcPauseMetric, i => i is >= 5 and <= 8 ? 9 : 0.5),
            Series(RuntimeHealthQueryBuilder.ExceptionsMetric, i => i is >= 30 and <= 33 ? 600 : 30));

        Assert.Equal([RuntimeFindingKind.ExceptionRate, RuntimeFindingKind.GcPressure], findings.Select(f => f.Kind));
    }

    [Fact]
    public void NoData_NoFindings() => Assert.Empty(RuntimeHealthDetector.Detect([], Start, End, Width));
}
