using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Model;

/// <summary>
/// JSON round-trip coverage for <see cref="DashboardRequest"/>/<see cref="Dashboard"/>
/// through the source-gen <see cref="DashboardsJsonContext"/> - same shape as
/// <see cref="SavedViewRequestJsonTests"/>, minus that type's <c>pageType</c> member (a
/// dashboard isn't scoped to one Explorer page - see <c>docs-internal/adr/0023-custom-dashboards.md</c>).
/// </summary>
public class DashboardRequestJsonTests
{
    // Deliberately omits "description" (the one optional member) - same
    // omitted-vs-default concern SavedViewRequestJsonTests covers.
    private const string MinimalJson = """{"name":"x","layoutJson":{"panels":[]}}""";

    [Fact]
    public void Deserialize_OmittedDescription_SurvivesAsNull()
    {
        var request = JsonSerializer.Deserialize(MinimalJson, DashboardsJsonContext.Default.DashboardRequest)!;

        Assert.Null(request.Description);
        Assert.Equal(JsonValueKind.Object, request.LayoutJson.ValueKind);
    }

    [Fact]
    public void Deserialize_ExplicitEmptyDescription_IsStillHonored()
    {
        const string json = """{"name":"x","description":"","layoutJson":{"panels":[]}}""";

        var request = JsonSerializer.Deserialize(json, DashboardsJsonContext.Default.DashboardRequest)!;

        Assert.Equal("", request.Description);
    }

    [Fact]
    public void Deserialize_ArbitraryNestedPanels_RoundTripsOpaquely()
    {
        // LayoutJson is never interpreted by Flare.Api (see Dashboard's remarks) - an
        // arbitrary panel array (mirroring a real Logs+Metrics panel pair) must survive
        // intact regardless of its contents.
        const string json = """
            {"name":"x","layoutJson":{"panels":[
                {"id":"p1","panelType":"Logs","title":"Errors","layout":{"x":0,"y":0,"w":12,"h":4},"query":{"search":"level:error"}},
                {"id":"p2","panelType":"Metrics","title":"Latency","layout":{"x":0,"y":4,"w":12,"h":4},"query":{"selectedMetric":{"metricName":"http.duration"}}}
            ]}}
            """;

        var request = JsonSerializer.Deserialize(json, DashboardsJsonContext.Default.DashboardRequest)!;

        var panels = request.LayoutJson.GetProperty("panels");
        Assert.Equal(2, panels.GetArrayLength());
        Assert.Equal("Logs", panels[0].GetProperty("panelType").GetString());
        Assert.Equal("http.duration", panels[1].GetProperty("query").GetProperty("selectedMetric").GetProperty("metricName").GetString());
    }

    [Fact]
    public void Serialize_Dashboard_UsesCamelCasePropertyNames()
    {
        var dashboard = new Dashboard
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Service health",
            LayoutJson = JsonDocument.Parse("""{"panels":[]}""").RootElement,
            CreatedAt = DateTimeOffset.Parse("2026-09-12T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-09-12T00:00:00Z"),
        };

        var json = JsonSerializer.Serialize(dashboard, DashboardsJsonContext.Default.Dashboard);

        Assert.Contains("\"name\":\"Service health\"", json);
        Assert.Contains("\"layoutJson\":{\"panels\":[]}", json);
    }
}
