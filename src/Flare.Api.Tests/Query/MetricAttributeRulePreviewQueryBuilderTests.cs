using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class MetricAttributeRulePreviewQueryBuilderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    private static MetricAttributeRulePreviewRequest Req(string metric, MetricAttributeRuleMode mode = MetricAttributeRuleMode.Drop, params string[] attrs) =>
        new() { MetricName = metric, Mode = mode, Attributes = attrs.Length == 0 ? ["user.id"] : attrs };

    [Theory]
    [InlineData("http.client.*", "http.client.duration", true)]
    [InlineData("http.client.*", "http.server.duration", false)]
    [InlineData("http.client.duration", "http.client.duration", true)]
    [InlineData("http.client.duration", "http.client.duration2", false)]
    public void Matches_ExactOrTrailingStarPrefix(string pattern, string name, bool expected) =>
        Assert.Equal(expected, MetricAttributeRulePreviewQueryBuilder.Matches(pattern, name));

    [Fact]
    public void BuildPreview_ExactName_UsesEqualityOverAllFourTables()
    {
        var result = MetricAttributeRulePreviewQueryBuilder.BuildPreview(Req("http.requests"), 60, Now);
        var parameters = result.Parameters.ToDictionary();

        Assert.Equal(4, Count(result.Sql, "MetricName = {metricName:String}"));
        Assert.Equal(3, Count(result.Sql, "UNION ALL"));
        Assert.Equal("http.requests", parameters["metricName"]);
        Assert.DoesNotContain("prefix", parameters.Keys);
    }

    [Fact]
    public void BuildPreview_Prefix_UsesStartsWithWithoutTheStar()
    {
        var result = MetricAttributeRulePreviewQueryBuilder.BuildPreview(Req("http.client.*"), 60, Now);

        Assert.Equal(4, Count(result.Sql, "startsWith(MetricName, {prefix:String})"));
        Assert.Equal("http.client.", result.Parameters.ToDictionary()["prefix"]);
    }

    [Fact]
    public void BuildPreview_Drop_KeepsKeysNotListed_KeepOnly_KeepsListed()
    {
        var drop = MetricAttributeRulePreviewQueryBuilder.BuildPreview(Req("m"), 60, Now).Sql;
        var keepOnly = MetricAttributeRulePreviewQueryBuilder.BuildPreview(Req("m", MetricAttributeRuleMode.KeepOnly), 60, Now).Sql;

        Assert.Contains("(k, v) -> NOT has({attrs:Array(String)}, k)", drop);
        Assert.Contains("(k, v) -> has({attrs:Array(String)}, k)", keepOnly);
        Assert.DoesNotContain("NOT has", keepOnly);
    }

    [Fact]
    public void BuildPreview_TrimsAndDedupesAttributes_AndCutsOneOverTheCap()
    {
        var result = MetricAttributeRulePreviewQueryBuilder.BuildPreview(Req("m", MetricAttributeRuleMode.Drop, " a ", "a", "b"), 60, Now);
        var parameters = result.Parameters.ToDictionary();

        Assert.Equal(new[] { "a", "b" }, Assert.IsType<string[]>(parameters["attrs"]));
        Assert.Equal((uint)(MetricAttributeRulePreviewQueryBuilder.MaxMetrics + 1), parameters["metricLimit"]);
    }

    [Fact]
    public void BuildMetricNames_ReadsDistinctNamesFromEveryTable_Capped()
    {
        var result = MetricAttributeRulePreviewQueryBuilder.BuildMetricNames(1440, Now);

        Assert.Equal(4, Count(result.Sql, "SELECT DISTINCT MetricName"));
        Assert.Equal((uint)MetricAttributeRulePreviewQueryBuilder.MaxMetricNames, result.Parameters.ToDictionary()["nameLimit"]);
        Assert.Equal(Now.AddMinutes(-1440).UtcDateTime, result.Parameters.ToDictionary()["from"]);
    }

    [Fact]
    public void PreviewRequest_ValidatesLikeARuleRequest()
    {
        Assert.Null(Req("http.client.*").Validate());
        Assert.NotNull(Req("*").Validate());
        Assert.NotNull((Req("m") with { Attributes = [] }).Validate());
    }

    private static int Count(string haystack, string needle) => haystack.Split(needle).Length - 1;
}
