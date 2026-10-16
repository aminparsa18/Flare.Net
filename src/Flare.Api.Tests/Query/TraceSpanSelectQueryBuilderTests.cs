using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class TraceSpanSelectQueryBuilderTests
{
    private static readonly TraceSpanCondition Root = new() { Name = "A", RootSpan = true };
    private static readonly TraceSpanCondition Server = new() { Name = "C", Kind = 2 };

    private static SelectedSpansRequest Request(bool wholeTrace, string[]? spanKeys = null, string[]? resourceKeys = null, string[]? eventKeys = null) =>
        new(["t1", "t2"], new TraceStructureFilter { Conditions = [Root, Server], Expression = "(A => C || A)" }, spanKeys ?? [], resourceKeys ?? [], eventKeys ?? [], wholeTrace);

    [Fact]
    public void Build_WholeTrace_ReadsEverySpanWithOneBitPerCondition()
    {
        var built = TraceSpanSelectQueryBuilder.Build(Request(wholeTrace: true));

        Assert.Equal(
            "SELECT TraceId, SpanId, ParentSpanId, Name, ServiceName, StartTime, DurationNano, Kind, StatusCode, " +
            "toUInt32(toUInt32((ParentSpanId = '')) * 1 + toUInt32((Kind = {tsCKind:UInt8})) * 4) AS MatchMask, " +
            "CAST(map(), 'Map(String, String)') AS SelectedSpanAttributes, CAST(map(), 'Map(String, String)') AS SelectedResourceAttributes, " +
            "CAST([], 'Array(Map(String, String))') AS SelectedEventAttributes\n" +
            "FROM spans WHERE TraceId IN {ssTraceIds:Array(String)}\n" +
            "ORDER BY TraceId, StartTime, SpanId\n" +
            "LIMIT {ssPerTrace:UInt32} BY TraceId",
            built.Sql);
        Assert.Equal(new[] { "t1", "t2" }, (string[])built.Parameters["ssTraceIds"].Value!);
        Assert.Equal(TraceSpanSelectQueryBuilder.MaxSpansPerTrace, built.Parameters["ssPerTrace"].Value);
    }

    [Fact]
    public void Build_OtherwiseOnlyTheSpansMatchingACondition()
    {
        var built = TraceSpanSelectQueryBuilder.Build(Request(wholeTrace: false));

        Assert.Contains("WHERE TraceId IN {ssTraceIds:Array(String)} AND ((ParentSpanId = '') OR (Kind = {tsCKind:UInt8}))", built.Sql);
    }

    [Fact]
    public void Build_ReadsOnlyTheRequestedKeys()
    {
        var built = TraceSpanSelectQueryBuilder.Build(Request(false, ["http.route"], ["k8s.pod.name"], ["exception.message", "exception.type"]));

        Assert.Contains("mapFilter((k, v) -> k IN {ssSpanKeys:Array(String)}, SpanAttributes) AS SelectedSpanAttributes", built.Sql);
        Assert.Contains("mapFilter((k, v) -> k IN {ssResourceKeys:Array(String)}, ResourceAttributes) AS SelectedResourceAttributes", built.Sql);
        Assert.Contains("arrayMap(m -> mapFilter((k, v) -> k IN {ssEventKeys:Array(String)}, m), `Events.Attributes`) AS SelectedEventAttributes", built.Sql);
        Assert.Equal(new[] { "exception.message", "exception.type" }, (string[])built.Parameters["ssEventKeys"].Value!);
    }

    [Fact]
    public void Build_RejectsNoTracesAndTooManyTraces()
    {
        var structure = new TraceStructureFilter { Conditions = [Root], Expression = "A" };

        Assert.Throws<ArgumentException>(() => TraceSpanSelectQueryBuilder.Build(new SelectedSpansRequest([], structure, [], [], [], false)));
        Assert.Throws<ArgumentException>(() => TraceSpanSelectQueryBuilder.Build(
            new SelectedSpansRequest(Enumerable.Range(0, TraceSpanSelectQueryBuilder.MaxTraces + 1).Select(i => $"t{i}").ToArray(), structure, [], [], [], false)));
    }

    [Fact]
    public void Build_AnEventAttributeConditionComparesEachEventsMap()
    {
        var condition = new TraceSpanCondition
        {
            Name = "A",
            Attributes = [new SpanAttributeFilter { Bag = SpanAttributeBag.Event, Key = "exception.message", Value = "boom", Operator = SpanAttributeFilterOperator.Equals }],
        };

        var built = TraceSpanSelectQueryBuilder.Build(new SelectedSpansRequest(["t1"], new TraceStructureFilter { Conditions = [condition], Expression = "A" }, [], [], [], false));

        Assert.Contains("arrayExists(m -> m[{attrKeyTA_0:String}] = {attrValueTA_0:String}, `Events.Attributes`)", built.Sql);
    }
}
