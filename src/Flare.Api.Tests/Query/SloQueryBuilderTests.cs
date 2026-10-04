using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class SloQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 37, TimeSpan.Zero);

    private static Slo MakeSlo(SloKind kind = SloKind.Availability, string operation = "", int thresholdMs = 0) => new()
    {
        Id = Guid.NewGuid(),
        Name = "checkout",
        ServiceName = "shop",
        OperationName = operation,
        Kind = kind,
        TargetPercent = 99.5,
        LatencyThresholdMs = thresholdMs,
        WindowDays = 28,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    [Fact]
    public void BadExpression_Availability_IsErrorCount() =>
        Assert.Equal("ErrorCount", SloQueryBuilder.BadExpression(MakeSlo()));

    [Fact]
    public void BadExpression_Latency_IsTotalMinusTheLadderColumn() =>
        Assert.Equal("TotalCount - Under500ms", SloQueryBuilder.BadExpression(MakeSlo(SloKind.Latency, thresholdMs: 500)));

    [Fact]
    public void BadExpression_LatencyOffTheLadder_Throws() =>
        Assert.Throws<InvalidOperationException>(() => SloQueryBuilder.BadExpression(MakeSlo(SloKind.Latency, thresholdMs: 300)));

    [Fact]
    public void BuildCounts_ReadsOneColumnPairPerWindow_FromThePreAggregate()
    {
        var query = SloQueryBuilder.BuildCounts(MakeSlo(), [3600, 300], Now);

        Assert.Contains("FROM span_sli_minute", query.Sql);
        Assert.Contains("sumIf(TotalCount, TimeBucket >= {f0:DateTime}) AS t0", query.Sql);
        Assert.Contains("sumIf(ErrorCount, TimeBucket >= {f1:DateTime}) AS b1", query.Sql);
        Assert.Contains("ServiceName = {service:String}", query.Sql);
        Assert.DoesNotContain("Name = {operation:String}", query.Sql);
    }

    [Fact]
    public void BuildCounts_ScopesToTheOperationWhenSet()
    {
        var query = SloQueryBuilder.BuildCounts(MakeSlo(operation: "POST /checkout"), [300], Now);

        Assert.Contains("AND Name = {operation:String}", query.Sql);
    }

    [Fact]
    public void BuildCounts_LatencyUsesTheThresholdColumn()
    {
        var query = SloQueryBuilder.BuildCounts(MakeSlo(SloKind.Latency, thresholdMs: 250), [300], Now);

        Assert.Contains("sumIf(TotalCount - Under250ms, TimeBucket >= {f0:DateTime}) AS b0", query.Sql);
    }

    [Fact]
    public void BuildSeries_GroupsByHourAcrossTheSloWindow()
    {
        var query = SloQueryBuilder.BuildSeries(MakeSlo(), Now);

        Assert.Contains("toStartOfHour(TimeBucket) AS Hour", query.Sql);
        Assert.Contains("GROUP BY Hour", query.Sql);
        Assert.Contains("ORDER BY Hour", query.Sql);
    }
}
