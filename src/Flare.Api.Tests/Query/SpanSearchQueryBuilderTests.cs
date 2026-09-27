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

    [Theory]
    [InlineData(SpanSortKey.StartTime, false, "ORDER BY StartTime DESC, TraceId DESC, SpanId DESC")]
    [InlineData(SpanSortKey.StartTime, true, "ORDER BY StartTime ASC, TraceId ASC, SpanId ASC")]
    [InlineData(SpanSortKey.Duration, false, "ORDER BY DurationNano DESC, TraceId DESC, SpanId DESC")]
    [InlineData(SpanSortKey.SpanCount, false, "ORDER BY rollup.SpanCount DESC, TraceId DESC, SpanId DESC")]
    public void Build_OrdersBy_RequestedSortKeyAndDirection(SpanSortKey sortBy, bool ascending, string expectedOrderBy)
    {
        var result = SpanSearchQueryBuilder.Build(new SpanSearchRequest { SortBy = sortBy, SortAscending = ascending }, Now);

        Assert.Contains(expectedOrderBy, result.Sql);
    }

    [Fact]
    public void Build_SortByDuration_WithCursor_ComparesDurationTuple()
    {
        var cursor = new SpanSearchCursor(SpanSortKey.Duration, false, 5_000_000UL, "0102030405060708090a0b0c0d0e0f10", "a1a2a3a4a5a6a7a8").Encode();

        var result = SpanSearchQueryBuilder.Build(new SpanSearchRequest { SortBy = SpanSortKey.Duration, Cursor = cursor }, Now);

        Assert.Contains(
            "(DurationNano, TraceId, SpanId) < ({cursorValue:UInt64}, {cursorTraceId:String}, {cursorSpanId:String})",
            result.Sql);
        Assert.Equal(5_000_000UL, result.Parameters.ToDictionary()["cursorValue"]);
    }

    [Fact]
    public void Build_Ascending_WithCursor_ComparesGreaterThan()
    {
        var cursor = new SpanSearchCursor(SpanSortKey.Duration, true, 5UL, "01", "a1").Encode();

        var result = SpanSearchQueryBuilder.Build(new SpanSearchRequest { SortBy = SpanSortKey.Duration, SortAscending = true, Cursor = cursor }, Now);

        Assert.Contains("(DurationNano, TraceId, SpanId) > (", result.Sql);
    }

    [Theory]
    [InlineData(SpanSortKey.Duration, false)] // re-sorted by another key
    [InlineData(SpanSortKey.StartTime, true)] // same key, flipped direction
    public void Build_WithCursorFromAnotherSort_TreatsRequestAsFirstPage(SpanSortKey sortBy, bool ascending)
    {
        var cursor = new SpanSearchCursor(Now, "0102030405060708090a0b0c0d0e0f10", "a1a2a3a4a5a6a7a8").Encode();

        var result = SpanSearchQueryBuilder.Build(new SpanSearchRequest { SortBy = sortBy, SortAscending = ascending, Cursor = cursor }, Now);

        Assert.DoesNotContain("cursorTraceId", result.Sql);
    }

    [Fact]
    public void Build_SortBySpanCount_JoinsPerTraceRollup_RestrictedToMatchingTraces()
    {
        var result = SpanSearchQueryBuilder.Build(
            new SpanSearchRequest { Filter = new SpanFilter { RootSpansOnly = true }, SortBy = SpanSortKey.SpanCount },
            Now);

        Assert.Contains(", rollup.SpanCount, rollup.HasError\nFROM spans\nGLOBAL INNER JOIN", result.Sql);
        Assert.Contains("count() AS SpanCount, countIf(StatusCode = {errorStatus:String}) > 0 AS HasError", result.Sql);
        Assert.Contains("WHERE TraceId GLOBAL IN (SELECT TraceId FROM spans WHERE StartTime >= {from:DateTime64(9)}", result.Sql);
        Assert.Contains(") AS rollup USING (TraceId)", result.Sql);
    }

    [Fact]
    public void Build_WithRootSpansOnly_IncludesItInTheWhereClause()
    {
        var result = SpanSearchQueryBuilder.Build(new SpanSearchRequest { Filter = new SpanFilter { RootSpansOnly = true } }, Now);

        Assert.Contains("ParentSpanId = ''", result.Sql);
    }
}
