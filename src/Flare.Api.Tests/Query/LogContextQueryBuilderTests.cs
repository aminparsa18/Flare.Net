using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class LogContextQueryBuilderTests
{
    private static readonly DateTimeOffset AnchorTimestamp = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid AnchorEventId = Guid.NewGuid();

    private static LogContextRequest Request(int? before = null, int? after = null) => new()
    {
        EventId = AnchorEventId,
        Timestamp = AnchorTimestamp,
        Before = before,
        After = after,
    };

    [Fact]
    public void Build_AnchorQuery_MatchesExactTimestampAndEventId_NotATupleComparison()
    {
        var result = LogContextQueryBuilder.Build(Request());

        Assert.Contains("WHERE Timestamp = {anchorTs:DateTime64(9)} AND EventId = {anchorId:UUID}", result.Anchor.Sql);
        var parameters = result.Anchor.Parameters.ToDictionary();
        Assert.Equal(AnchorTimestamp.UtcDateTime, parameters["anchorTs"]);
        Assert.Equal(AnchorEventId, parameters["anchorId"]);
    }

    [Fact]
    public void Build_BeforeQuery_UsesLessThanTupleComparison_OrderedNewestFirst()
    {
        var result = LogContextQueryBuilder.Build(Request());

        Assert.Contains("WHERE (Timestamp, EventId) < ({anchorTs:DateTime64(9)}, {anchorId:UUID})", result.Before.Sql);
        Assert.Contains("ORDER BY Timestamp DESC, EventId DESC", result.Before.Sql);
    }

    [Fact]
    public void Build_AfterQuery_UsesGreaterThanTupleComparison_OrderedOldestFirst()
    {
        var result = LogContextQueryBuilder.Build(Request());

        Assert.Contains("WHERE (Timestamp, EventId) > ({anchorTs:DateTime64(9)}, {anchorId:UUID})", result.After.Sql);
        Assert.Contains("ORDER BY Timestamp ASC, EventId ASC", result.After.Sql);
    }

    [Fact]
    public void Build_EveryQuery_SelectsEveryLogEventColumn_FromLogsTable()
    {
        var result = LogContextQueryBuilder.Build(Request());

        const string expectedColumns = "SELECT EventId, Timestamp, ObservedTimestamp, TraceId, SpanId, TraceFlags, SeverityText, " +
            "SeverityNumber, ServiceName, Body, ResourceSchemaUrl, ResourceAttributes, ScopeSchemaUrl, ScopeName, " +
            "ScopeVersion, ScopeAttributes, LogAttributes, EventName";
        Assert.Contains(expectedColumns, result.Anchor.Sql);
        Assert.Contains(expectedColumns, result.Before.Sql);
        Assert.Contains(expectedColumns, result.After.Sql);
        Assert.Contains("FROM logs", result.Anchor.Sql);
        Assert.Contains("FROM logs", result.Before.Sql);
        Assert.Contains("FROM logs", result.After.Sql);
    }

    [Fact]
    public void Build_DefaultSize_RequestsOneMoreRow_ToDetectMore_InBothDirections()
    {
        var result = LogContextQueryBuilder.Build(Request());

        Assert.Equal(LogContextQueryBuilder.DefaultSize, result.BeforeLimit);
        Assert.Equal(LogContextQueryBuilder.DefaultSize, result.AfterLimit);
        Assert.Equal((uint)(LogContextQueryBuilder.DefaultSize + 1), result.Before.Parameters.ToDictionary()["beforeLimit"]);
        Assert.Equal((uint)(LogContextQueryBuilder.DefaultSize + 1), result.After.Parameters.ToDictionary()["afterLimit"]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(5000, LogContextQueryBuilder.MaxSize)]
    public void Build_ClampsBeforeAndAfter_ToValidRange(int requested, int expectedClamped)
    {
        var result = LogContextQueryBuilder.Build(Request(before: requested, after: requested));

        Assert.Equal(expectedClamped, result.BeforeLimit);
        Assert.Equal(expectedClamped, result.AfterLimit);
    }

    [Fact]
    public void Build_BeforeAndAfter_CanBeClampedIndependently()
    {
        var result = LogContextQueryBuilder.Build(Request(before: 10, after: 90));

        Assert.Equal(10, result.BeforeLimit);
        Assert.Equal(90, result.AfterLimit);
    }

    [Fact]
    public void Build_WithResourceAttributeSource_ScopesBeforeAndAfterOnly()
    {
        var result = LogContextQueryBuilder.Build(Request(), ("k8s.pod.name", "pod-a"));

        Assert.Contains("AND ResourceAttributes[{srcKey:String}] = {srcVal:String}", result.Before.Sql);
        Assert.Contains("AND ResourceAttributes[{srcKey:String}] = {srcVal:String}", result.After.Sql);
        Assert.DoesNotContain("srcVal", result.Anchor.Sql);
        var parameters = result.Before.Parameters.ToDictionary();
        Assert.Equal("k8s.pod.name", parameters["srcKey"]);
        Assert.Equal("pod-a", parameters["srcVal"]);
    }

    [Fact]
    public void Build_WithServiceNameSource_UsesServiceNameColumn()
    {
        var result = LogContextQueryBuilder.Build(Request(), ("service.name", "api"));

        Assert.Contains("AND ServiceName = {srcVal:String}", result.After.Sql);
        Assert.DoesNotContain("srcKey", result.After.Sql);
    }

    [Fact]
    public void Build_WithoutSource_HasNoSourceClause() =>
        Assert.DoesNotContain("srcVal", LogContextQueryBuilder.Build(Request()).Before.Sql);

    private static LogEventDto Anchor(string service, params (string, string)[] resource) => new()
    {
        EventId = AnchorEventId,
        Timestamp = AnchorTimestamp,
        ObservedTimestamp = AnchorTimestamp,
        IngestedAt = AnchorTimestamp,
        TraceId = "",
        SpanId = "",
        TraceFlags = 0,
        SeverityText = "",
        SeverityNumber = 0,
        ServiceName = service,
        Body = "",
        ResourceSchemaUrl = "",
        ResourceAttributes = resource.ToDictionary(r => r.Item1, r => r.Item2),
        ScopeSchemaUrl = "",
        ScopeName = "",
        ScopeVersion = "",
        ScopeAttributes = new Dictionary<string, string>(),
        LogAttributes = new Dictionary<string, string>(),
        EventName = "",
        PatternId = "",
        PatternTemplate = "",
    };

    [Fact]
    public void ResolveSource_PrefersPodThenContainerThenHostThenService()
    {
        Assert.Equal(("k8s.pod.name", "p"), LogContextQueryBuilder.ResolveSource(Anchor("svc", ("host.name", "h"), ("container.id", "c"), ("k8s.pod.name", "p"))));
        Assert.Equal(("container.id", "c"), LogContextQueryBuilder.ResolveSource(Anchor("svc", ("host.name", "h"), ("container.id", "c"))));
        Assert.Equal(("host.name", "h"), LogContextQueryBuilder.ResolveSource(Anchor("svc", ("host.name", "h"))));
        Assert.Equal(("service.name", "svc"), LogContextQueryBuilder.ResolveSource(Anchor("svc")));
        Assert.Null(LogContextQueryBuilder.ResolveSource(Anchor("")));
    }
}
