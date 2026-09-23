using Flare.Api.Model;
using Flare.Api.Query.LogQl;
using Xunit;

namespace Flare.Api.Tests.Query.LogQl;

public class LogQlParserTests
{
    [Fact]
    public void Parse_SelectCountFromStream_NoWhereOrGroupBy()
    {
        var query = LogQlParser.Parse("select count(*) from stream");

        var select = Assert.IsType<LogQlSelectAggregate>(query.Select);
        Assert.Equal(LogQlAggFunc.Count, select.Func);
        Assert.Null(select.Column);
        Assert.Null(query.Where);
        Assert.Null(query.GroupBy);
    }

    [Fact]
    public void Parse_SelectStarFromStream_IsRawSelect()
    {
        var query = LogQlParser.Parse("select * from stream");

        Assert.IsType<LogQlSelectStar>(query.Select);
    }

    [Fact]
    public void Parse_SelectColumnList_IsColumnsSelect()
    {
        var query = LogQlParser.Parse("select Service, Body from stream");

        var select = Assert.IsType<LogQlSelectColumns>(query.Select);
        Assert.Equal([LogQlColumn.Service, LogQlColumn.Body], select.Columns);
    }

    [Theory]
    [InlineData("avg", LogQlAggFunc.Avg)]
    [InlineData("sum", LogQlAggFunc.Sum)]
    public void Parse_AvgOrSumOfSeverityNumber_IsAggregateSelect(string func, LogQlAggFunc expected)
    {
        var query = LogQlParser.Parse($"select {func}(SeverityNumber) from stream");

        var select = Assert.IsType<LogQlSelectAggregate>(query.Select);
        Assert.Equal(expected, select.Func);
        Assert.Equal(LogQlColumn.SeverityNumber, select.Column);
    }

    [Theory]
    [InlineData("avg")]
    [InlineData("sum")]
    public void Parse_AvgOrSumOfNonNumericColumn_Throws(string func)
    {
        Assert.Throws<LogQlParseException>(() => LogQlParser.Parse($"select {func}(Service) from stream"));
    }

    [Fact]
    public void Parse_SeverityNumberInWhere_Throws()
    {
        var ex = Assert.Throws<LogQlParseException>(() => LogQlParser.Parse("select * from stream where SeverityNumber = '5'"));
        Assert.Contains("SeverityNumber", ex.Message);
    }

    [Fact]
    public void Parse_ColumnSelectWithGroupBy_Throws()
    {
        Assert.Throws<LogQlParseException>(() => LogQlParser.Parse("select Service from stream group by time(1h)"));
    }

    [Fact]
    public void Parse_IsCaseInsensitive_ForKeywordsAndColumns()
    {
        var query = LogQlParser.Parse("SELECT COUNT(*) FROM STREAM WHERE SERVICE = 'checkout'");

        Assert.IsType<LogQlSelectAggregate>(query.Select);
        var comparison = Assert.IsType<LogQlComparison>(query.Where);
        Assert.Equal(LogQlColumn.Service, comparison.Column);
    }

    [Fact]
    public void Parse_GroupByTimeOnly_SetsBucketSeconds_AndNoSecondaryGrouping()
    {
        var query = LogQlParser.Parse("select count(*) from stream group by time(1h)");

        Assert.NotNull(query.GroupBy);
        Assert.Equal(3600, query.GroupBy!.TimeBucketSeconds);
        Assert.Equal(LogAggregateGroupBy.None, query.GroupBy.Secondary);
    }

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("15m", 900)]
    [InlineData("1h", 3600)]
    [InlineData("7d", 604_800)]
    public void Parse_GroupByTime_SupportsEachDurationUnit(string duration, int expectedSeconds)
    {
        var query = LogQlParser.Parse($"select count(*) from stream group by time({duration})");

        Assert.Equal(expectedSeconds, query.GroupBy!.TimeBucketSeconds);
    }

    [Fact]
    public void Parse_GroupByTimeAndService_SetsSecondaryGrouping()
    {
        var query = LogQlParser.Parse("select count(*) from stream group by time(15m), service");

        Assert.Equal(LogAggregateGroupBy.Service, query.GroupBy!.Secondary);
    }

    [Fact]
    public void Parse_GroupByTimeAndLevel_SetsSecondaryGrouping()
    {
        var query = LogQlParser.Parse("select count(*) from stream group by time(15m), level");

        Assert.Equal(LogAggregateGroupBy.Level, query.GroupBy!.Secondary);
    }

    [Fact]
    public void Parse_WhereWithAndOrNotAndParens_BuildsExpectedTree()
    {
        var query = LogQlParser.Parse(
            "select * from stream where Service = 'a' and (Level = 'Error' or not Level = 'Warning')");

        var and = Assert.IsType<LogQlBinary>(query.Where);
        Assert.Equal(LogQlBoolOp.And, and.Op);
        Assert.IsType<LogQlComparison>(and.Left);
        var or = Assert.IsType<LogQlBinary>(and.Right);
        Assert.Equal(LogQlBoolOp.Or, or.Op);
        Assert.IsType<LogQlComparison>(or.Left);
        Assert.IsType<LogQlNot>(or.Right);
    }

    [Fact]
    public void Parse_Like_ProducesLikeComparison()
    {
        var query = LogQlParser.Parse("select * from stream where Body like '%timeout%'");

        var comparison = Assert.IsType<LogQlComparison>(query.Where);
        Assert.Equal(LogQlOp.Like, comparison.Op);
        Assert.Equal("%timeout%", comparison.Literal);
    }

    [Fact]
    public void Parse_NotLike_ProducesNotLikeComparison()
    {
        var query = LogQlParser.Parse("select * from stream where Body not like '%ok%'");

        var comparison = Assert.IsType<LogQlComparison>(query.Where);
        Assert.Equal(LogQlOp.NotLike, comparison.Op);
    }

    [Theory]
    [InlineData("=", LogQlOp.Eq)]
    [InlineData("!=", LogQlOp.NotEq)]
    [InlineData("<>", LogQlOp.NotEq)]
    [InlineData("<", LogQlOp.Lt)]
    [InlineData("<=", LogQlOp.Lte)]
    [InlineData(">", LogQlOp.Gt)]
    [InlineData(">=", LogQlOp.Gte)]
    public void Parse_ComparisonOperators_MapToExpectedOp(string opText, LogQlOp expectedOp)
    {
        var query = LogQlParser.Parse($"select * from stream where Level {opText} 'Error'");

        var comparison = Assert.IsType<LogQlComparison>(query.Where);
        Assert.Equal(expectedOp, comparison.Op);
    }

    [Fact]
    public void Parse_EscapedQuoteInStringLiteral_UnescapesToASingleQuote()
    {
        var query = LogQlParser.Parse("select * from stream where Body = 'it''s broken'");

        var comparison = Assert.IsType<LogQlComparison>(query.Where);
        Assert.Equal("it's broken", comparison.Literal);
    }

    [Fact]
    public void Parse_MissingFromStream_Throws()
    {
        var ex = Assert.Throws<LogQlParseException>(() => LogQlParser.Parse("select count(*)"));
        Assert.Contains("from", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_FromNonStreamTable_Throws()
    {
        Assert.Throws<LogQlParseException>(() => LogQlParser.Parse("select count(*) from logs"));
    }

    [Fact]
    public void Parse_UnknownColumn_ThrowsWithColumnNameInMessage()
    {
        var ex = Assert.Throws<LogQlParseException>(() => LogQlParser.Parse("select * from stream where Foo = 'x'"));
        Assert.Contains("Foo", ex.Message);
    }

    [Fact]
    public void Parse_GroupByWithoutCountStar_Throws()
    {
        var ex = Assert.Throws<LogQlParseException>(() => LogQlParser.Parse("select * from stream group by time(1h)"));
        Assert.Contains("count(*)", ex.Message);
    }

    [Fact]
    public void Parse_InvalidDurationUnit_Throws()
    {
        Assert.Throws<LogQlParseException>(() => LogQlParser.Parse("select count(*) from stream group by time(1x)"));
    }

    [Fact]
    public void Parse_UnterminatedStringLiteral_Throws()
    {
        var ex = Assert.Throws<LogQlParseException>(() => LogQlParser.Parse("select * from stream where Body = 'abc"));
        Assert.Contains("Unterminated", ex.Message);
    }

    [Fact]
    public void Parse_TrailingGarbageAfterGroupBy_Throws()
    {
        Assert.Throws<LogQlParseException>(() => LogQlParser.Parse("select count(*) from stream group by time(1h) extra"));
    }

    [Fact]
    public void Parse_MissingSelectList_Throws()
    {
        Assert.Throws<LogQlParseException>(() => LogQlParser.Parse("select from stream"));
    }

    [Fact]
    public void Parse_JsonComparison_ProducesJsonComparisonNode()
    {
        var query = LogQlParser.Parse("select * from stream where json(Body, 'user.id') = '42'");

        var comparison = Assert.IsType<LogQlJsonComparison>(query.Where);
        Assert.Equal("user.id", comparison.Path);
        Assert.Equal(LogQlOp.Eq, comparison.Op);
        Assert.Equal("42", comparison.Literal);
    }

    [Fact]
    public void Parse_JsonComparisonIsCaseInsensitive_ForJsonKeywordAndBody()
    {
        var query = LogQlParser.Parse("select * from stream where JSON(BODY, 'user.id') = '42'");

        Assert.IsType<LogQlJsonComparison>(query.Where);
    }

    [Fact]
    public void Parse_JsonComparisonWithLike_ProducesLikeComparison()
    {
        var query = LogQlParser.Parse("select * from stream where json(Body, 'user.name') like '%mith%'");

        var comparison = Assert.IsType<LogQlJsonComparison>(query.Where);
        Assert.Equal(LogQlOp.Like, comparison.Op);
        Assert.Equal("%mith%", comparison.Literal);
    }

    [Fact]
    public void Parse_JsonComparisonCombinedWithColumnComparison_BuildsExpectedTree()
    {
        var query = LogQlParser.Parse("select * from stream where Service = 'checkout' and json(Body, 'user.id') = '42'");

        var and = Assert.IsType<LogQlBinary>(query.Where);
        Assert.IsType<LogQlComparison>(and.Left);
        Assert.IsType<LogQlJsonComparison>(and.Right);
    }

    [Fact]
    public void Parse_JsonComparisonNonBodyColumn_Throws()
    {
        Assert.Throws<LogQlParseException>(() => LogQlParser.Parse("select * from stream where json(Service, 'user.id') = '42'"));
    }

    [Fact]
    public void Parse_JsonComparisonEmptyPath_Throws()
    {
        var ex = Assert.Throws<LogQlParseException>(() => LogQlParser.Parse("select * from stream where json(Body, '') = '42'"));
        Assert.Contains("must not be empty", ex.Message);
    }

    [Fact]
    public void Parse_JsonComparisonMissingComma_Throws()
    {
        Assert.Throws<LogQlParseException>(() => LogQlParser.Parse("select * from stream where json(Body 'user.id') = '42'"));
    }

    [Theory]
    [InlineData("log", LogQlAttributeBag.Log)]
    [InlineData("resource", LogQlAttributeBag.Resource)]
    [InlineData("scope", LogQlAttributeBag.Scope)]
    public void Parse_AttrComparison_ResolvesEachBag(string bagText, LogQlAttributeBag expectedBag)
    {
        var query = LogQlParser.Parse($"select * from stream where attr({bagText}, 'http.status_code') = '200'");

        var comparison = Assert.IsType<LogQlAttributeComparison>(query.Where);
        Assert.Equal(expectedBag, comparison.Bag);
        Assert.Equal("http.status_code", comparison.Key);
        Assert.Equal(LogQlOp.Eq, comparison.Op);
        Assert.Equal("200", comparison.Literal);
    }

    [Fact]
    public void Parse_AttrComparisonIsCaseInsensitive_ForAttrKeywordAndBag()
    {
        var query = LogQlParser.Parse("select * from stream where ATTR(LOG, 'foo') = 'bar'");

        Assert.IsType<LogQlAttributeComparison>(query.Where);
    }

    [Fact]
    public void Parse_AttrComparisonWithLike_ProducesLikeComparison()
    {
        var query = LogQlParser.Parse("select * from stream where attr(log, 'user.name') like '%mith%'");

        var comparison = Assert.IsType<LogQlAttributeComparison>(query.Where);
        Assert.Equal(LogQlOp.Like, comparison.Op);
        Assert.Equal("%mith%", comparison.Literal);
    }

    [Fact]
    public void Parse_AttrHas_ProducesExistsNode_NotNegated()
    {
        var query = LogQlParser.Parse("select * from stream where attr(resource, 'k8s.pod.name') has");

        var exists = Assert.IsType<LogQlAttributeExists>(query.Where);
        Assert.Equal(LogQlAttributeBag.Resource, exists.Bag);
        Assert.Equal("k8s.pod.name", exists.Key);
        Assert.False(exists.Negate);
    }

    [Fact]
    public void Parse_AttrNotHas_ProducesExistsNode_Negated()
    {
        var query = LogQlParser.Parse("select * from stream where attr(scope, 'foo') not has");

        var exists = Assert.IsType<LogQlAttributeExists>(query.Where);
        Assert.True(exists.Negate);
    }

    [Fact]
    public void Parse_AttrComparisonCombinedWithColumnComparison_BuildsExpectedTree()
    {
        var query = LogQlParser.Parse("select * from stream where Service = 'checkout' and attr(log, 'foo') has");

        var and = Assert.IsType<LogQlBinary>(query.Where);
        Assert.IsType<LogQlComparison>(and.Left);
        Assert.IsType<LogQlAttributeExists>(and.Right);
    }

    [Fact]
    public void Parse_AttrComparisonUnknownBag_ThrowsWithBagNameInMessage()
    {
        var ex = Assert.Throws<LogQlParseException>(() => LogQlParser.Parse("select * from stream where attr(bogus, 'foo') = 'x'"));
        Assert.Contains("bogus", ex.Message);
    }

    [Fact]
    public void Parse_AttrComparisonEmptyKey_Throws()
    {
        var ex = Assert.Throws<LogQlParseException>(() => LogQlParser.Parse("select * from stream where attr(log, '') = 'x'"));
        Assert.Contains("must not be empty", ex.Message);
    }

    [Fact]
    public void Parse_AttrComparisonMissingComma_Throws()
    {
        Assert.Throws<LogQlParseException>(() => LogQlParser.Parse("select * from stream where attr(log 'foo') = 'x'"));
    }
}
