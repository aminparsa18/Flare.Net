using System.Text.Json;
using Flare.Api.Model;

namespace Flare.Api.Query;

/// <summary>
/// Finds which dashboard panels read a given metric, by scanning each <see cref="Dashboard.LayoutJson"/>
/// - the roadmap's "Dashboards using this metric" lookup. Pure and ClickHouse-free.
/// </summary>
/// <remarks>
/// <see cref="Dashboard.LayoutJson"/> is deliberately opaque to the rest of Flare.Api, so this reads
/// it structurally rather than deserializing into a type: only <c>Metrics</c> panels are considered,
/// and a panel references a metric through <c>query.selectedMetric.metricName</c> (single mode) or
/// <c>query.formulaQueries[].metric.metricName</c> (Formula mode) - see the dashboard's
/// <c>MetricsSavedViewState</c>. Anything malformed or of an unexpected shape is skipped, never thrown on.
/// Every dashboard is visible to every authenticated user (ADR-0027 narrows only who may mutate),
/// so no per-caller filtering applies.
/// </remarks>
public static class MetricDashboardUsageFinder
{
    public static IReadOnlyList<MetricDashboardUsage> Find(IEnumerable<Dashboard> dashboards, string metricName)
    {
        var result = new List<MetricDashboardUsage>();
        foreach (var dashboard in dashboards.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var panels = FindPanels(dashboard.LayoutJson, metricName);
            if (panels.Count > 0)
            {
                result.Add(new MetricDashboardUsage { DashboardId = dashboard.Id, DashboardName = dashboard.Name, Panels = panels });
            }
        }

        return result;
    }

    private static List<MetricDashboardPanelUsage> FindPanels(JsonElement layout, string metricName)
    {
        var panels = new List<MetricDashboardPanelUsage>();
        if (layout.ValueKind != JsonValueKind.Object
            || !layout.TryGetProperty("panels", out var panelArray)
            || panelArray.ValueKind != JsonValueKind.Array)
        {
            return panels;
        }

        foreach (var panel in panelArray.EnumerateArray())
        {
            if (panel.ValueKind != JsonValueKind.Object
                || GetString(panel, "panelType") != "Metrics"
                || !panel.TryGetProperty("query", out var query)
                || query.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var single = Child(query, "selectedMetric") is { } selected && GetString(selected, "metricName") == metricName;
            var formula = query.TryGetProperty("formulaQueries", out var formulaQueries)
                && formulaQueries.ValueKind == JsonValueKind.Array
                && formulaQueries.EnumerateArray().Any(q => Child(q, "metric") is { } metric && GetString(metric, "metricName") == metricName);

            if (single || formula)
            {
                panels.Add(new MetricDashboardPanelUsage
                {
                    PanelId = GetString(panel, "id") ?? "",
                    Title = GetString(panel, "title") ?? "",
                    InFormula = !single,
                });
            }
        }

        return panels;
    }

    private static JsonElement? Child(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.Object
            ? child
            : null;

    private static string? GetString(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
