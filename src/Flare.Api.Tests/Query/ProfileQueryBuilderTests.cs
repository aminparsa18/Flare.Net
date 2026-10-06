using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class ProfileQueryBuilderTests
{
    private static readonly DateTimeOffset End = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(null, ProfileQueryBuilder.DefaultWindowMinutes)]
    [InlineData(0, ProfileQueryBuilder.DefaultWindowMinutes)]
    [InlineData(15, 15)]
    [InlineData(100_000, ProfileQueryBuilder.MaxWindowMinutes)]
    public void ClampWindowMinutes_ClampsAndDefaults(int? requested, int expected)
    {
        Assert.Equal(expected, ProfileQueryBuilder.ClampWindowMinutes(requested));
    }

    [Fact]
    public void BuildFlameGraph_FiltersServiceAndSampleType_AsBoundParameters()
    {
        var built = ProfileQueryBuilder.BuildFlameGraph(new FlameGraphRequest { Service = "checkout'; DROP", SampleType = "cpu" }, 60, End);

        Assert.Contains("ServiceName = {service:String}", built.Sql);
        Assert.Contains("SampleType = {sampleType:String}", built.Sql);
        Assert.DoesNotContain("DROP", built.Sql);
        Assert.DoesNotContain("TraceId", built.Sql);
    }

    [Fact]
    public void BuildFlameGraph_NarrowsToTheSpan_WhenTraceAndSpanAreGiven()
    {
        var built = ProfileQueryBuilder.BuildFlameGraph(
            new FlameGraphRequest { Service = "s", SampleType = "cpu", TraceId = "ABC", SpanId = "DEF" }, 60, End);

        Assert.Contains("TraceId = {traceId:String}", built.Sql);
        Assert.Contains("SpanId = {spanId:String}", built.Sql);
    }

    [Fact]
    public void BuildFlameGraph_GroupsByStack_HeaviestFirst_AndFetchesOneRowOverTheCap()
    {
        var built = ProfileQueryBuilder.BuildFlameGraph(new FlameGraphRequest { Service = "s", SampleType = "cpu" }, 60, End);

        Assert.Contains("GROUP BY StackHash, Stack", built.Sql);
        Assert.Contains("ORDER BY Total DESC", built.Sql);
        Assert.Contains("LIMIT {limit:UInt32}", built.Sql);
    }

    [Fact]
    public void BuildTypes_GroupsBySeries_WithoutAServiceFilter()
    {
        var built = ProfileQueryBuilder.BuildTypes(60, End);

        Assert.Contains("GROUP BY Service, SampleType, SampleUnit", built.Sql);
        Assert.DoesNotContain("{service:String}", built.Sql);
    }

    [Fact]
    public void Where_AppliesTheProjectScope()
    {
        ServiceScope.Current = ["checkout", "web-*"];
        try
        {
            var built = ProfileQueryBuilder.BuildTypes(60, End);

            Assert.Contains("scopeExact", built.Sql);
        }
        finally
        {
            ServiceScope.Current = null;
        }
    }
}
