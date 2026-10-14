using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class TraceMatchedSpansQueryBuilderTests
{
    private static readonly TraceSpanCondition Checkout = new() { Name = "A", ServiceName = "checkout", SpanName = "POST /pay", SpanNameMatch = SpanNameMatch.NotEquals };
    private static readonly TraceSpanCondition PaymentError = new() { Name = "B", ServiceName = "payment", StatusCode = "STATUS_CODE_ERROR" };

    [Fact]
    public void Build_SelectsMatchingSpansOfTheTraces_LimitedPerTrace()
    {
        var built = TraceMatchedSpansQueryBuilder.Build(new MatchedSpansRequest(
            ["t1", "t2"],
            new TraceStructureFilter { Conditions = [Checkout, PaymentError], Expression = "A => B" },
            3));

        Assert.Equal(
            "SELECT TraceId, SpanId, ServiceName, Name, StartTime, DurationNano, count() OVER (PARTITION BY TraceId) AS Matched\n" +
            "FROM spans WHERE TraceId IN {msTraceIds:Array(String)} AND " +
            "((ServiceName = {tsAService:String} AND Name != {tsAName:String}) OR (ServiceName = {tsBService:String} AND StatusCode = {tsBStatus:String}))\n" +
            "ORDER BY TraceId, StartTime, SpanId\n" +
            "LIMIT {msPerTrace:UInt32} BY TraceId",
            built.Sql);
        Assert.Equal(new[] { "t1", "t2" }, (string[])built.Parameters["msTraceIds"].Value!);
        Assert.Equal(3, built.Parameters["msPerTrace"].Value);
    }

    [Fact]
    public void Build_ClampsSpansPerTrace()
    {
        var structure = new TraceStructureFilter { Conditions = [PaymentError], Expression = "B" };

        Assert.Equal(TraceMatchedSpansQueryBuilder.MaxSpansPerTrace, TraceMatchedSpansQueryBuilder.Build(new MatchedSpansRequest(["t"], structure, 5000)).Parameters["msPerTrace"].Value);
        Assert.Equal(1, TraceMatchedSpansQueryBuilder.Build(new MatchedSpansRequest(["t"], structure, 0)).Parameters["msPerTrace"].Value);
    }

    [Fact]
    public void Build_AppliesTheServiceScope()
    {
        ServiceScope.Current = ["checkout"];
        try
        {
            var built = TraceMatchedSpansQueryBuilder.Build(new MatchedSpansRequest(["t"], new TraceStructureFilter { Conditions = [PaymentError], Expression = "B" }, 3));

            Assert.Contains("ServiceName IN {scopeExact:Array(String)}", built.Sql);
        }
        finally
        {
            ServiceScope.Current = null;
        }
    }

    [Fact]
    public void Build_RejectsNoTraces() =>
        Assert.Throws<ArgumentException>(() => TraceMatchedSpansQueryBuilder.Build(new MatchedSpansRequest([], new TraceStructureFilter { Conditions = [PaymentError], Expression = "B" }, 3)));
}
