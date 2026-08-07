using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class LogSearchQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_WithNullFilter_DoesNotThrow_AndAppliesDefaultTimeRange()
    {
        // request.Filter's `= new()` initializer only applies to C# callers - System.Text.Json
        // deserialization overwrites init-only properties back to null when the JSON
        // body omits them (confirmed live against a real `{}` POST body). Regression
        // test for that exact shape, not just the C#-default-constructed one.
        var result = LogSearchQueryBuilder.Build(new LogSearchRequest { Filter = null! }, Now);

        Assert.Equal((Now - LogFilterSqlBuilder.DefaultLookback).UtcDateTime, result.Parameters.ToDictionary()["from"]);
    }

    [Fact]
    public void Build_SelectsEveryLogEventColumn_FromLogsTable()
    {
        var result = LogSearchQueryBuilder.Build(new LogSearchRequest(), Now);

        Assert.Contains("SELECT EventId, Timestamp, ObservedTimestamp, TraceId, SpanId, TraceFlags, SeverityText, " +
            "SeverityNumber, ServiceName, Body, ResourceSchemaUrl, ResourceAttributes, ScopeSchemaUrl, ScopeName, " +
            "ScopeVersion, ScopeAttributes, LogAttributes, EventName", result.Sql);
        Assert.Contains("FROM logs", result.Sql);
        Assert.Contains("ORDER BY Timestamp DESC, EventId DESC", result.Sql);
    }

    [Fact]
    public void Build_DefaultPageSize_RequestsOneMoreRow_ToDetectNextPage()
    {
        var result = LogSearchQueryBuilder.Build(new LogSearchRequest(), Now);

        Assert.Equal(LogSearchQueryBuilder.DefaultPageSize, result.PageSize);
        Assert.Equal((uint)(LogSearchQueryBuilder.DefaultPageSize + 1), result.Parameters.ToDictionary()["limit"]);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(5000, LogSearchQueryBuilder.MaxPageSize)]
    public void Build_ClampsPageSize_ToValidRange(int requested, int expectedClamped)
    {
        var result = LogSearchQueryBuilder.Build(new LogSearchRequest { PageSize = requested }, Now);

        Assert.Equal(expectedClamped, result.PageSize);
    }

    [Fact]
    public void Build_WithoutCursor_OmitsTupleComparison()
    {
        var result = LogSearchQueryBuilder.Build(new LogSearchRequest(), Now);

        Assert.DoesNotContain("cursorTs", result.Sql);
        Assert.DoesNotContain("(Timestamp, EventId) <", result.Sql);
    }

    [Fact]
    public void Build_WithCursor_AddsKeysetTupleComparison_AndBindsItsParts()
    {
        var timestamp = new DateTimeOffset(2026, 8, 7, 11, 59, 0, TimeSpan.Zero);
        var eventId = Guid.NewGuid();
        var cursor = new LogSearchCursor(timestamp, eventId).Encode();

        var result = LogSearchQueryBuilder.Build(new LogSearchRequest { Cursor = cursor }, Now);

        Assert.Contains("(Timestamp, EventId) < ({cursorTs:DateTime64(9)}, {cursorId:UUID})", result.Sql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal(timestamp.UtcDateTime, parameters["cursorTs"]);
        Assert.Equal(eventId, parameters["cursorId"]);
    }

    [Fact]
    public void Build_WithMalformedCursor_TreatsRequestAsFirstPage()
    {
        var result = LogSearchQueryBuilder.Build(new LogSearchRequest { Cursor = "not-a-valid-cursor!!" }, Now);

        Assert.DoesNotContain("cursorTs", result.Sql);
    }
}
