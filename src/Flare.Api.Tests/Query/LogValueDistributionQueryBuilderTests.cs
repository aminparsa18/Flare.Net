using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class LogValueDistributionQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private static LogValueDistributionRequest Request(string attributeKey = "duration_ms", int sampleSize = 4000, LogFilter? filter = null) =>
        new() { AttributeKey = attributeKey, SampleSize = sampleSize, Filter = filter ?? new LogFilter() };

    [Fact]
    public void Build_WithNullFilter_DoesNotThrow()
    {
        // See LogSearchQueryBuilderTests' equivalent test: System.Text.Json overwrites
        // request.Filter's default back to null when "filter" is absent from the body.
        var result = LogValueDistributionQueryBuilder.Build(Request(filter: null!), Now);

        Assert.Contains("WHERE Timestamp >=", result.Sql);
    }

    [Fact]
    public void Build_BindsAttributeKeyAndSampleSizeAsParameters()
    {
        var result = LogValueDistributionQueryBuilder.Build(Request(attributeKey: "latency_ms", sampleSize: 500), Now);

        Assert.Contains("LogAttributes[{attributeKey:String}]", result.Sql);
        Assert.Contains("LIMIT {sampleSize:UInt32}", result.Sql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("latency_ms", parameters["attributeKey"]);
        Assert.Equal(500, parameters["sampleSize"]);
    }

    [Fact]
    public void Build_SamplesRandomly_NotChronologically()
    {
        var result = LogValueDistributionQueryBuilder.Build(Request(), Now);

        // A random sample, not the first n events chronologically - see this builder's own
        // remarks on why that distinction matters for what the chart shows.
        Assert.Contains("ORDER BY rand()", result.Sql);
        Assert.DoesNotContain("ORDER BY Timestamp", result.Sql);
    }

    [Fact]
    public void Build_FiltersOutNullValues_InOuterQuery_NotSameSelectLevelAsAlias()
    {
        var result = LogValueDistributionQueryBuilder.Build(Request(), Now);

        // Value's null-check has to be in a query level above where the alias is defined -
        // see this builder's own remarks on why "AS Value ... WHERE Value IS NOT NULL" in
        // one SELECT isn't safe to rely on.
        Assert.Contains("AS Value", result.Sql);
        Assert.Contains("WHERE Value IS NOT NULL", result.Sql);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Build_NonPositiveSampleSize_Throws(int sampleSize)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LogValueDistributionQueryBuilder.Build(Request(sampleSize: sampleSize), Now));
    }

    [Fact]
    public void Build_EmptyAttributeKey_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LogValueDistributionQueryBuilder.Build(Request(attributeKey: ""), Now));
    }
}
