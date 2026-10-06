using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Model;

public class LogMetricRequestTests
{
    private static LogMetricRequest Req(string metric = "logs.checkout.errors", params string[] groupBy) =>
        new() { Name = "n", MetricName = metric, GroupBy = groupBy };

    [Theory]
    [InlineData("logs.checkout.errors")]
    [InlineData("a")]
    [InlineData("Log_Errors-total")]
    public void Valid_metric_names_pass(string metric) => Assert.Null(Req(metric).Validate());

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("1errors")]
    [InlineData("has space")]
    [InlineData("logs.*")]
    public void Invalid_metric_names_fail(string metric) => Assert.NotNull(Req(metric).Validate());

    [Fact]
    public void Requires_a_name() => Assert.NotNull((Req() with { Name = " " }).Validate());

    [Fact]
    public void Group_by_is_optional_but_keys_must_not_be_blank()
    {
        Assert.Null((Req() with { GroupBy = null }).Validate());
        Assert.NotNull(Req("m", " ").Validate());
    }

    [Fact]
    public void Group_by_is_capped_on_distinct_keys()
    {
        Assert.Null(Req("m", "a", "b", "c", "d", "e", "e").Validate());
        Assert.NotNull(Req("m", "a", "b", "c", "d", "e", "f").Validate());
    }
}
