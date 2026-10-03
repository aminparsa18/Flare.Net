using System.Text.Json;
using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class MetricDashboardUsageFinderTests
{
    private const string Metric = "http.server.duration";

    private static Dashboard Dash(string name, string layoutJson) => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        LayoutJson = JsonDocument.Parse(layoutJson.Replace("METRIC", Metric)).RootElement,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public void Find_MatchesSingleAndFormulaPanels_OrderedByDashboardName()
    {
        var b = Dash("B board", """{"panels":[{"id":"p1","panelType":"Metrics","title":"Latency","query":{"selectedMetric":{"metricName":"METRIC"}}}]}""");
        var a = Dash("a board", """{"panels":[{"id":"p2","panelType":"Metrics","title":"Ratio","query":{"formulaQueries":[{"metric":null},{"metric":{"metricName":"METRIC"}}]}}]}""");

        var result = MetricDashboardUsageFinder.Find([b, a], Metric);

        Assert.Equal(["a board", "B board"], result.Select(r => r.DashboardName));
        Assert.True(result[0].Panels[0].InFormula);
        Assert.False(result[1].Panels[0].InFormula);
        Assert.Equal("p1", result[1].Panels[0].PanelId);
    }

    [Fact]
    public void Find_IgnoresOtherMetricsNonMetricsPanelsAndMalformedLayouts()
    {
        var other = Dash("other", """{"panels":[{"id":"p","panelType":"Metrics","title":"x","query":{"selectedMetric":{"metricName":"cpu"}}}]}""");
        var logs = Dash("logs", """{"panels":[{"id":"p","panelType":"Logs","title":"x","query":{"selectedMetric":{"metricName":"METRIC"}}}]}""");
        var junk1 = Dash("junk1", "[]");
        var junk2 = Dash("junk2", """{"panels":[1,{"panelType":"Metrics","query":"x"},{"panelType":"Metrics","query":{"selectedMetric":null,"formulaQueries":{}}}]}""");

        Assert.Empty(MetricDashboardUsageFinder.Find([other, logs, junk1, junk2], Metric));
    }

    [Fact]
    public void Find_ListsEveryMatchingPanelOnADashboard()
    {
        var d = Dash("d", """{"panels":[{"id":"1","panelType":"Metrics","title":"A","query":{"selectedMetric":{"metricName":"METRIC"}}},{"id":"2","panelType":"Metrics","title":"B","query":{"selectedMetric":{"metricName":"METRIC"}}}]}""");

        var result = Assert.Single(MetricDashboardUsageFinder.Find([d], Metric));
        Assert.Equal(["A", "B"], result.Panels.Select(p => p.Title));
    }
}
