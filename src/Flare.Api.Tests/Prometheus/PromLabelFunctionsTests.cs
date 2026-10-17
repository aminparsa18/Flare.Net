using Flare.Api.Prometheus;
using Xunit;

namespace Flare.Api.Tests.Prometheus;

public class PromLabelFunctionsTests
{
    private static PromSeries Series(params (string k, string v)[] labels)
    {
        var s = new PromSeries(labels.ToDictionary(l => l.k, l => l.v));
        s.Samples.Add(new PromSample(0, 1));
        return s;
    }

    private static List<PromSeries> Run(string query, params PromSeries[] input) =>
        PromLabelFunctions.Apply((PromLabelFunction)PromQlParser.Parse(query), [.. input]);

    [Fact]
    public void LabelReplace_ExpandsCaptureGroups_AndKeepsMetricName()
    {
        var result = Assert.Single(Run(
            "label_replace(x, \"host\", \"$1\", \"instance\", \"(.*):\\\\d+\")",
            Series(("__name__", "x"), ("instance", "web-1:9100"))));

        Assert.Equal("web-1", result.Labels["host"]);
        Assert.Equal("x", result.Labels["__name__"]);
    }

    [Fact]
    public void LabelReplace_NoMatch_LeavesSeriesUntouched_AndRegexIsAnchored()
    {
        var result = Assert.Single(Run(
            "label_replace(x, \"host\", \"h\", \"instance\", \"web\")",
            Series(("instance", "web-1"))));

        Assert.DoesNotContain("host", result.Labels.Keys);
    }

    [Fact]
    public void LabelReplace_EmptyExpansion_RemovesDestinationLabel()
    {
        var result = Assert.Single(Run(
            "label_replace(x, \"env\", \"\", \"env\", \".*\")",
            Series(("env", "prod"), ("job", "api"))));

        Assert.DoesNotContain("env", result.Labels.Keys);
        Assert.Equal("api", result.Labels["job"]);
    }

    [Fact]
    public void LabelJoin_JoinsSourcesWithSeparator()
    {
        var result = Assert.Single(Run(
            "label_join(x, \"id\", \"/\", \"job\", \"zone\")",
            Series(("job", "api"), ("zone", "a"))));

        Assert.Equal("api/a", result.Labels["id"]);
    }

    [Fact]
    public void CollidingLabelSets_Fail()
    {
        var ex = Assert.Throws<PromQlException>(() => Run(
            "label_replace(x, \"host\", \"same\", \"host\", \".*\")",
            Series(("host", "a")),
            Series(("host", "b"))));

        Assert.Contains("same labelset", ex.Message);
    }

    [Fact]
    public void InvalidDestinationLabel_Fails() =>
        Assert.Throws<PromQlException>(() => Run("label_join(x, \"bad-name\", \",\", \"a\")", Series(("a", "1"))));
}
