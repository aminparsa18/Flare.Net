using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;
using Flare.Api.Query.LogQl;
using Xunit;

namespace Flare.Api.Tests.Query.LogQl;

public class LogQlWhereTranslatorTests
{
    [Fact]
    public void Translate_SimpleEquality_BindsLiteralAsParameter_NotInterpolated()
    {
        var parameters = new ClickHouseParameterCollection();
        var sql = LogQlWhereTranslator.Translate(new LogQlComparison(LogQlColumn.Service, LogQlOp.Eq, "checkout"), parameters);

        Assert.Equal("ServiceName = {qlp0:String}", sql);
        Assert.Equal("checkout", parameters.ToDictionary()["qlp0"]);
        Assert.DoesNotContain("checkout", sql);
    }

    [Theory]
    [InlineData(LogQlOp.Eq, "=")]
    [InlineData(LogQlOp.NotEq, "!=")]
    [InlineData(LogQlOp.Lt, "<")]
    [InlineData(LogQlOp.Lte, "<=")]
    [InlineData(LogQlOp.Gt, ">")]
    [InlineData(LogQlOp.Gte, ">=")]
    public void Translate_ComparisonOperators_MapToSqlOperator(LogQlOp op, string expectedSqlOp)
    {
        var parameters = new ClickHouseParameterCollection();
        var sql = LogQlWhereTranslator.Translate(new LogQlComparison(LogQlColumn.Level, op, "Error"), parameters);

        Assert.Equal($"SeverityText {expectedSqlOp} {{qlp0:String}}", sql);
    }

    [Fact]
    public void Translate_Like_UsesIlike_AndBindsLiteralAsWritten_NoAutoWildcards()
    {
        var parameters = new ClickHouseParameterCollection();
        var sql = LogQlWhereTranslator.Translate(new LogQlComparison(LogQlColumn.Body, LogQlOp.Like, "%timeout%"), parameters);

        Assert.Equal("Body ILIKE {qlp0:String}", sql);
        Assert.Equal("%timeout%", parameters.ToDictionary()["qlp0"]);
    }

    [Fact]
    public void Translate_NotLike_NegatesTheIlikeFragment()
    {
        var parameters = new ClickHouseParameterCollection();
        var sql = LogQlWhereTranslator.Translate(new LogQlComparison(LogQlColumn.Body, LogQlOp.NotLike, "%ok%"), parameters);

        Assert.Equal("NOT (Body ILIKE {qlp0:String})", sql);
    }

    [Theory]
    [InlineData(LogQlColumn.Service, "ServiceName")]
    [InlineData(LogQlColumn.Level, "SeverityText")]
    [InlineData(LogQlColumn.Body, "Body")]
    [InlineData(LogQlColumn.TraceId, "TraceId")]
    [InlineData(LogQlColumn.SpanId, "SpanId")]
    public void Translate_EachColumn_MapsToItsRealColumnName(LogQlColumn column, string expectedColumnName)
    {
        var parameters = new ClickHouseParameterCollection();
        var sql = LogQlWhereTranslator.Translate(new LogQlComparison(column, LogQlOp.Eq, "x"), parameters);

        Assert.StartsWith($"{expectedColumnName} = ", sql);
    }

    [Fact]
    public void Translate_And_WrapsBothSidesInParens_AndUsesUniqueParameterNames()
    {
        var parameters = new ClickHouseParameterCollection();
        var expr = new LogQlBinary(
            LogQlBoolOp.And,
            new LogQlComparison(LogQlColumn.Service, LogQlOp.Eq, "a"),
            new LogQlComparison(LogQlColumn.Level, LogQlOp.Eq, "b"));

        var sql = LogQlWhereTranslator.Translate(expr, parameters);

        Assert.Equal("(ServiceName = {qlp0:String} AND SeverityText = {qlp1:String})", sql);
        var dict = parameters.ToDictionary();
        Assert.Equal("a", dict["qlp0"]);
        Assert.Equal("b", dict["qlp1"]);
    }

    [Fact]
    public void Translate_Or_UsesOrKeyword()
    {
        var parameters = new ClickHouseParameterCollection();
        var expr = new LogQlBinary(
            LogQlBoolOp.Or,
            new LogQlComparison(LogQlColumn.Service, LogQlOp.Eq, "a"),
            new LogQlComparison(LogQlColumn.Service, LogQlOp.Eq, "b"));

        var sql = LogQlWhereTranslator.Translate(expr, parameters);

        Assert.Equal("(ServiceName = {qlp0:String} OR ServiceName = {qlp1:String})", sql);
    }

    [Fact]
    public void Translate_Not_WrapsOperandInNotParens()
    {
        var parameters = new ClickHouseParameterCollection();
        var expr = new LogQlNot(new LogQlComparison(LogQlColumn.Service, LogQlOp.Eq, "a"));

        var sql = LogQlWhereTranslator.Translate(expr, parameters);

        Assert.Equal("NOT (ServiceName = {qlp0:String})", sql);
    }

    [Fact]
    public void Translate_JsonComparison_EmitsOneParameterPerPathSegment_ThenTheValue()
    {
        var parameters = new ClickHouseParameterCollection();
        var sql = LogQlWhereTranslator.Translate(new LogQlJsonComparison("user.id", LogQlOp.Eq, "42"), parameters);

        Assert.Equal("JSONExtractString(Body, {qlp0:String}, {qlp1:String}) = {qlp2:String}", sql);
        var dict = parameters.ToDictionary();
        Assert.Equal("user", dict["qlp0"]);
        Assert.Equal("id", dict["qlp1"]);
        Assert.Equal("42", dict["qlp2"]);
    }

    [Fact]
    public void Translate_JsonComparisonSinglePathSegment_EmitsOneKeyArgument()
    {
        var parameters = new ClickHouseParameterCollection();
        var sql = LogQlWhereTranslator.Translate(new LogQlJsonComparison("status", LogQlOp.NotEq, "500"), parameters);

        Assert.Equal("JSONExtractString(Body, {qlp0:String}) != {qlp1:String}", sql);
    }

    [Fact]
    public void Translate_JsonComparisonWithLike_UsesIlikeAgainstJsonExtractString()
    {
        var parameters = new ClickHouseParameterCollection();
        var sql = LogQlWhereTranslator.Translate(new LogQlJsonComparison("user.name", LogQlOp.Like, "%mith%"), parameters);

        Assert.Equal("JSONExtractString(Body, {qlp0:String}, {qlp1:String}) ILIKE {qlp2:String}", sql);
    }

    [Fact]
    public void Translate_JsonComparisonCombinedWithColumnComparison_UsesUniqueParameterNamesAcrossBoth()
    {
        var parameters = new ClickHouseParameterCollection();
        var expr = new LogQlBinary(
            LogQlBoolOp.And,
            new LogQlComparison(LogQlColumn.Service, LogQlOp.Eq, "checkout"),
            new LogQlJsonComparison("user.id", LogQlOp.Eq, "42"));

        var sql = LogQlWhereTranslator.Translate(expr, parameters);

        Assert.Equal(
            "(ServiceName = {qlp0:String} AND JSONExtractString(Body, {qlp1:String}, {qlp2:String}) = {qlp3:String})",
            sql);
    }
}
