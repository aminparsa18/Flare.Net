using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class SpanDurationPercentileQueryBuilderTests
{
    private static readonly DateTimeOffset Start = new(2026, 8, 7, 12, 0, 0, TimeSpan.Zero);

    private static SpanDurationPercentileRequest Request() =>
        new() { ServiceName = "shop-api", Name = "GET /orders", DurationNano = 42_000_000, StartTime = Start };

    [Fact]
    public void Build_FiltersBySameServiceAndName()
    {
        var result = SpanDurationPercentileQueryBuilder.Build(Request());

        Assert.Contains("FROM spans", result.Sql);
        Assert.Contains("ServiceName = {serviceName:String} AND Name = {spanName:String}", result.Sql);
        var parameters = result.Parameters.ToDictionary();
        Assert.Equal("shop-api", parameters["serviceName"]);
        Assert.Equal("GET /orders", parameters["spanName"]);
    }

    [Fact]
    public void Build_WindowIsCentredOnStartTime()
    {
        var parameters = SpanDurationPercentileQueryBuilder.Build(Request()).Parameters.ToDictionary();

        Assert.Equal(Start.UtcDateTime.AddHours(-1), parameters["from"]);
        Assert.Equal(Start.UtcDateTime.AddHours(1), parameters["to"]);
    }

    [Fact]
    public void Build_RanksAgainstTheSpansOwnDuration()
    {
        var result = SpanDurationPercentileQueryBuilder.Build(Request());

        Assert.Contains("countIf(DurationNano <= {duration:UInt64})", result.Sql);
        Assert.Equal(42_000_000UL, result.Parameters.ToDictionary()["duration"]);
    }
}
