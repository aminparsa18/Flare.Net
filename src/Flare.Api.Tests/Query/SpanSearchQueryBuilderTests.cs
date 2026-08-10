using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class SpanSearchQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_WithNullFilter_DoesNotThrow_AndAppliesDefaultTimeRange()
    {
        // Same STJ init-only-property regression this repo already guards for on the
        // logs side - see LogSearchQueryBuilderTests' identical case.
        var result = SpanSearchQueryBuilder.Build(new SpanSearchRequest { Filter = null! }, Now);

        Assert.Equal((Now - SpanFilterSqlBuilder.DefaultLookback).UtcDateTime, result.Parameters.ToDictionary()["from"]);
    }

    [Fact]
    public void Build_SelectsEverySpanColumn_FromSpansTable()
    {
        var result = SpanSearchQueryBuilder.Build(new SpanSearchRequest(), Now);

        Assert.Contains("SELECT TraceId, SpanId, ParentSpanId, TraceState, Name, Kind, StartTime, EndTime, " +
            "DurationNano, StatusCode, StatusMessage, ServiceName, ResourceSchemaUrl, ResourceAttributes, " +
            "ScopeSchemaUrl, ScopeName, ScopeVersion, ScopeAttributes, SpanAttributes, `Events.TimeUnixNano`, " +
            "`Events.Name`, `Events.Attributes`", result.Sql);
        Assert.Contains("FROM spans", result.Sql);
        Assert.Contains("ORDER BY StartTime DESC, TraceId DESC, SpanId DESC", result.Sql);
    }

    [Fact]
    public void Build_DefaultPageSize_RequestsOneMoreRow_ToDetectNextPage()
    {
        var result = SpanSearchQueryBuilder.Build(new SpanSearchRequest(), Now);

        Assert.Equal(SpanSearchQueryBuilder.DefaultPageSize, result.PageSize);
        Assert.Equal((uint)(SpanSearchQueryBuilder.DefaultPageSize + 1), result.Parameters.ToDictionary()["limit"]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(5000, SpanSearchQueryBuilder.MaxPageSize)]
    public void Build_ClampsPageSize_ToValidRange(int requested, int expectedClamped)
    {
        var result = SpanSearchQueryBuilder.Build(new SpanSearchRequest { PageSize = requested }, Now);

        Assert.Equal(expectedClamped, result.PageSize);
    }

    [Fact]
    public void Build_WithoutCursor_OmitsTupleComparison()
    {
        var result = SpanSearchQueryBuilder.Build(new SpanSearchRequest(), Now);

        Assert.DoesNotContain("cursorTs", result.Sql);
        Assert.DoesNotContain("(StartTime, TraceId, SpanId) <", result.Sql);
    }

    [Fact]
    public void Build_WithCursor_AddsKeysetTupleComparison_AndBindsItsParts()
    {
        var startTime = new DateTimeOffset(2026, 8, 10, 11, 59, 0, TimeSpan.Zero);
        var cursor = new SpanSearchCursor(startTime, "0102030405060708090a0b0c0d0e0f10", "a1a2a3a4a5a6a7a8").Encode();

        var result = SpanSearchQueryBuilder.Build(new SpanSearchRequest { Cursor = cursor }, Now);

        Assert.Contains(
            "(StartTime, TraceId, SpanId) < ({cursorTs:DateTime64(9)}, {cursorTraceId:String}, {cursorSpanId:String})",
            result.Sql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(startTime.UtcDateTime, parameters["cursorTs"]);
        Assert.Equal("0102030405060708090a0b0c0d0e0f10", parameters["cursorTraceId"]);
        Assert.Equal("a1a2a3a4a5a6a7a8", parameters["cursorSpanId"]);
    }

    [Fact]
    public void Build_WithMalformedCursor_TreatsRequestAsFirstPage()
    {
        var result = SpanSearchQueryBuilder.Build(new SpanSearchRequest { Cursor = "not-a-valid-cursor!!" }, Now);

        Assert.DoesNotContain("cursorTs", result.Sql);
    }

    [Fact]
    public void Build_WithRootSpansOnly_IncludesItInTheWhereClause()
    {
        var result = SpanSearchQueryBuilder.Build(new SpanSearchRequest { Filter = new SpanFilter { RootSpansOnly = true } }, Now);

        Assert.Contains("ParentSpanId = ''", result.Sql);
    }
}
