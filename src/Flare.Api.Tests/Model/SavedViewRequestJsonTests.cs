using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Model;

/// <summary>
/// JSON round-trip coverage for <see cref="SavedViewRequest"/>/<see cref="SavedView"/>
/// through the source-gen <see cref="SavedViewsJsonContext"/> - same
/// omitted-optional-member concern <see cref="AlertRuleRequestJsonTests"/> covers for
/// <see cref="AlertRuleRequest"/>, plus the opaque <see cref="JsonElement"/> state payload
/// this DTO uniquely carries (the dashboard's own TypeScript-owned filter shape,
/// round-tripped without Flare.Api ever interpreting its contents - see
/// <see cref="SavedView"/>'s remarks).
/// </summary>
public class SavedViewRequestJsonTests
{
    // Deliberately omits "description" (the one optional member) to isolate the same
    // System.Text.Json source-gen "omitted → default(T)" concern AlertRuleRequestJsonTests
    // covers - description has no bool/int-style "omitted vs. explicit default" collision
    // (there's no meaningful difference between omitted and ""), but the DTO still uses
    // the nullable-and-coalesce-in-the-service shape for consistency.
    private const string MinimalJson = """{"name":"x","pageType":"Logs","state":{"timeRangePreset":"1h","services":[]}}""";

    [Fact]
    public void Deserialize_OmittedDescription_SurvivesAsNull()
    {
        var request = JsonSerializer.Deserialize(MinimalJson, SavedViewsJsonContext.Default.SavedViewRequest)!;

        Assert.Null(request.Description);
        Assert.Equal(SavedViewPageType.Logs, request.PageType);
    }

    [Fact]
    public void Deserialize_ExplicitEmptyDescription_IsStillHonored()
    {
        const string json = """{"name":"x","description":"","pageType":"Traces","state":{}}""";

        var request = JsonSerializer.Deserialize(json, SavedViewsJsonContext.Default.SavedViewRequest)!;

        Assert.Equal("", request.Description);
    }

    [Fact]
    public void Deserialize_PageType_UsesCamelCaseStringEnum()
    {
        const string json = """{"name":"x","pageType":"Metrics","state":{}}""";

        var request = JsonSerializer.Deserialize(json, SavedViewsJsonContext.Default.SavedViewRequest)!;

        Assert.Equal(SavedViewPageType.Metrics, request.PageType);
    }

    [Fact]
    public void Deserialize_ArbitraryNestedState_RoundTripsOpaquely()
    {
        // State is never interpreted by Flare.Api (see SavedView's remarks) - an
        // arbitrary, deeply-nested shape (mirroring e.g. MetricsExplorerState's
        // filter+selectedMetric combo) must survive intact regardless of its contents.
        const string json = """
            {"name":"x","pageType":"Metrics","state":{
                "timeRangePreset":"6h",
                "services":["svc-a","svc-b"],
                "selectedMetric":{"metricName":"http.requests","serviceName":"svc-a","type":"Sum"}
            }}
            """;

        var request = JsonSerializer.Deserialize(json, SavedViewsJsonContext.Default.SavedViewRequest)!;

        Assert.Equal(JsonValueKind.Object, request.State.ValueKind);
        Assert.Equal("6h", request.State.GetProperty("timeRangePreset").GetString());
        Assert.Equal(2, request.State.GetProperty("services").GetArrayLength());
        Assert.Equal("http.requests", request.State.GetProperty("selectedMetric").GetProperty("metricName").GetString());
    }

    [Fact]
    public void Serialize_SavedView_UsesCamelCasePropertyNames()
    {
        var view = new SavedView
        {
            Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Name = "Errors last hour",
            PageType = SavedViewPageType.Logs,
            State = JsonDocument.Parse("""{"timeRangePreset":"1h"}""").RootElement,
            CreatedAt = DateTimeOffset.Parse("2026-08-10T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-08-10T00:00:00Z"),
        };

        var json = JsonSerializer.Serialize(view, SavedViewsJsonContext.Default.SavedView);

        Assert.Contains("\"pageType\":\"Logs\"", json);
        Assert.Contains("\"name\":\"Errors last hour\"", json);
        Assert.Contains("\"state\":{\"timeRangePreset\":\"1h\"}", json);
    }
}
