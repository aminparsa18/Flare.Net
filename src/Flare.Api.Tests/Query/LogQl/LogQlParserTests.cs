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

        Assert.Equal(LogQlSelectKind.Count, query.Select);
        Assert.Null(query.Where);
        Assert.Null(query.GroupBy);
    }

    [Fact]
    public void Parse_SelectStarFromStream_IsRawSelect()
    {
        var query = LogQlParser.Parse("select * from stream");

        Assert.Equal(LogQlSelectKind.Raw, query.Select);
    }

    [Fact]
    public void Parse_IsCaseInsensitive_ForKeywordsAndColumns()
    {
        var query = LogQlParser.Parse("SELECT COUNT(*) FROM STREAM WHERE SERVICE = 'checkout'");

        Assert.Equal(LogQlSelectKind.Count, query.Select);
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
}
