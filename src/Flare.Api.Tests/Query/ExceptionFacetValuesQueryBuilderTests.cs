using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ExceptionFacetValuesQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Build_ServiceField_GroupsByServiceName_OverTypedExceptionEvents()
    {
        var result = ExceptionFacetValuesQueryBuilder.Build(new ExceptionFacetValuesRequest { Field = ExceptionFacetField.Service }, Now);

        Assert.StartsWith("SELECT ServiceName AS Value, count() AS Cnt\n", result.Sql, StringComparison.Ordinal);
        Assert.Contains("ARRAY JOIN Events.TimeUnixNano AS EventTime, Events.Name AS EventName, Events.Attributes AS EventAttributes", result.Sql);
        Assert.Contains("EventName = {eventName:String}", result.Sql);
        Assert.Contains("EventAttributes['exception.type'] != ''", result.Sql);
        Assert.DoesNotContain("mapContains", result.Sql);
    }

    [Fact]
    public void Build_ResourceAttributeField_ReadsAndRequiresTheKey()
    {
        var result = ExceptionFacetValuesQueryBuilder.Build(
            new ExceptionFacetValuesRequest { Field = ExceptionFacetField.ResourceAttribute, Key = "deployment.environment" },
            Now);

        Assert.StartsWith("SELECT ResourceAttributes[{valuesKey:String}] AS Value", result.Sql, StringComparison.Ordinal);
        Assert.Contains("mapContains(ResourceAttributes, {valuesKey:String})", result.Sql);
        Assert.Equal("deployment.environment", result.Parameters.ToDictionary()["valuesKey"]);
    }

    [Fact]
    public void Build_ResourceAttributeField_WithoutKey_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ExceptionFacetValuesQueryBuilder.Build(new ExceptionFacetValuesRequest { Field = ExceptionFacetField.ResourceAttribute }, Now));
    }

    [Fact]
    public void Build_AppliesTheExceptionFilter()
    {
        var result = ExceptionFacetValuesQueryBuilder.Build(
            new ExceptionFacetValuesRequest
            {
                Filter = new ExceptionFilter
                {
                    Services = ["checkout"],
                    ResourceAttributes = [new ResourceAttributeFilter { Key = "service.version", Value = "2.3" }],
                },
            },
            Now);

        Assert.Contains("ServiceName IN {services:Array(String)}", result.Sql);
        Assert.Contains("ResourceAttributes[{ResAttrKey0:String}] = {ResAttrValue0:String}", result.Sql);
    }

    [Theory]
    [InlineData(null, 50u)]
    [InlineData(0, 50u)]
    [InlineData(10, 10u)]
    [InlineData(100_000, 500u)]
    public void Build_ClampsLimit(int? requested, uint expected)
    {
        var result = ExceptionFacetValuesQueryBuilder.Build(new ExceptionFacetValuesRequest { Limit = requested }, Now);

        Assert.Equal(expected, result.Parameters.ToDictionary()["valuesLimit"]);
    }
}
