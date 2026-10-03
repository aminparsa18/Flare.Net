using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Model;

public class MetricAttributeRuleRequestTests
{
    private static MetricAttributeRuleRequest Req(string metric = "http.requests", params string[] attrs) =>
        new() { Name = "r", MetricName = metric, Mode = MetricAttributeRuleMode.Drop, Attributes = attrs.Length == 0 ? ["user.id"] : attrs };

    [Theory]
    [InlineData("http.requests")]
    [InlineData("http.client.*")]
    public void Valid_metric_names_pass(string metric) => Assert.Null(Req(metric).Validate());

    [Theory]
    [InlineData("*")]
    [InlineData("http.*.duration")]
    [InlineData("  ")]
    public void Invalid_metric_names_fail(string metric) => Assert.NotNull(Req(metric).Validate());

    [Fact]
    public void Requires_at_least_one_non_blank_attribute()
    {
        Assert.NotNull((Req() with { Attributes = [] }).Validate());
        Assert.NotNull((Req() with { Attributes = [" "] }).Validate());
        Assert.NotNull((Req() with { Attributes = null }).Validate());
    }
}
