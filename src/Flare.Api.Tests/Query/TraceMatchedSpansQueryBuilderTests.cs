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
            "SELECT TraceId, SpanId, ServiceName, Name, StartTime, DurationNano, count() OVER (PARTITION BY TraceId) AS Matched, " +
            "CAST(map(), 'Map(String, String)') AS EchoedSpanAttributes, CAST(map(), 'Map(String, String)') AS EchoedResourceAttributes\n" +
            "FROM spans WHERE TraceId IN {msTraceIds:Array(String)} AND " +
            "((ServiceName = {tsAService:String} AND Name != {tsAName:String}) OR (ServiceName = {tsBService:String} AND StatusCode = {tsBStatus:String}))\n" +
            "ORDER BY TraceId, StartTime, SpanId\n" +
            "LIMIT {msPerTrace:UInt32} BY TraceId",
            built.Sql);
        Assert.Equal(new[] { "t1", "t2" }, (string[])built.Parameters["msTraceIds"].Value!);
        Assert.Equal(3, built.Parameters["msPerTrace"].Value);
        var parameters = built.Parameters.ToDictionary();
        Assert.False(parameters.ContainsKey("msSpanKeys"));
        Assert.False(parameters.ContainsKey("msResourceKeys"));
    }

    [Fact]
    public void Build_ReadsOutOnlyTheAttributesTheConditionsCompareOn()
    {
        var condition = new TraceSpanCondition
        {
            Name = "A",
            Attributes =
            [
                new SpanAttributeFilter { Key = "http.route", Value = "/cart" },
                new SpanAttributeFilter { Bag = SpanAttributeBag.Resource, Key = "k8s.namespace.name", Value = "prod" },
            ],
        };

        var built = TraceMatchedSpansQueryBuilder.Build(new MatchedSpansRequest(["t"], new TraceStructureFilter { Conditions = [condition], Expression = "A" }, 3));

        Assert.Contains("mapFilter((k, v) -> k IN {msSpanKeys:Array(String)}, SpanAttributes) AS EchoedSpanAttributes", built.Sql);
        Assert.Contains("mapFilter((k, v) -> k IN {msResourceKeys:Array(String)}, ResourceAttributes) AS EchoedResourceAttributes", built.Sql);
        Assert.Equal(new[] { "http.route" }, (string[])built.Parameters["msSpanKeys"].Value!);
        Assert.Equal(new[] { "k8s.namespace.name" }, (string[])built.Parameters["msResourceKeys"].Value!);
    }

    [Fact]
    public void Build_LeavesTheBagUnreadWhenNoConditionNamesAnAttributeInIt()
    {
        var condition = new TraceSpanCondition { Name = "A", Attributes = [new SpanAttributeFilter { Key = "http.route", Value = "/cart" }] };

        var built = TraceMatchedSpansQueryBuilder.Build(new MatchedSpansRequest(["t"], new TraceStructureFilter { Conditions = [condition], Expression = "A" }, 3));

        Assert.Contains("CAST(map(), 'Map(String, String)') AS EchoedResourceAttributes", built.Sql);
        Assert.DoesNotContain("ResourceAttributes)", built.Sql);
    }

    [Fact]
    public void EchoedKeys_ListsEveryConditionsAttributesOnceInConditionOrder()
    {
        var b = new TraceSpanCondition { Name = "B", Attributes = [new SpanAttributeFilter { Key = "db.system.name", Value = "x" }, new SpanAttributeFilter { Key = "http.route", Value = "y" }] };
        var a = new TraceSpanCondition
        {
            Name = "A",
            Attributes =
            [
                new SpanAttributeFilter { Key = "http.route", Value = "/cart" },
                new SpanAttributeFilter { Key = "http.route", Value = "/pay", Operator = SpanAttributeFilterOperator.NotEquals },
                new SpanAttributeFilter { Bag = SpanAttributeBag.Scope, Key = "scope.only", Value = "z" },
            ],
        };

        var (span, resource) = TraceMatchedSpansQueryBuilder.EchoedKeys([b, a]);

        Assert.Equal(new[] { "http.route", "db.system.name", "db.system" }, span);
        Assert.Empty(resource);
    }

    [Fact]
    public void EchoedKeys_ExpandsSemconvAliasesAndCapsTheTotal()
    {
        var status = new TraceSpanCondition { Name = "A", Attributes = [new SpanAttributeFilter { Key = "http.status_code", Value = "500" }] };
        Assert.Equal(new[] { "http.status_code", "http.response.status_code" }, TraceMatchedSpansQueryBuilder.EchoedKeys([status]).Span);

        var many = new TraceSpanCondition
        {
            Name = "A",
            Attributes = Enumerable.Range(0, 30).Select(i => new SpanAttributeFilter { Bag = i % 2 == 0 ? SpanAttributeBag.Span : SpanAttributeBag.Resource, Key = $"k{i}", Value = "v" }).ToArray(),
        };
        var (span, resource) = TraceMatchedSpansQueryBuilder.EchoedKeys([many]);
        Assert.Equal(TraceMatchedSpansQueryBuilder.MaxEchoedAttributes, span.Length + resource.Length);
    }

    [Fact]
    public void MergeEchoed_ListsSpanAttributesThenResourceOnesByKey_DroppingEmptyAndRepeated()
    {
        var merged = TraceMatchedSpansQueryBuilder.MergeEchoed(
            new Dictionary<string, string> { ["http.route"] = "/cart", ["a.key"] = "1", ["gone"] = "" },
            new Dictionary<string, string> { ["k8s.namespace.name"] = "prod", ["http.route"] = "ignored", ["a.resource"] = "2" });

        Assert.Equal(
            new[] { "a.key=1", "http.route=/cart", "a.resource=2", "k8s.namespace.name=prod" },
            merged.Select(a => $"{a.Key}={a.Value}"));
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
