namespace Flare.Api.Model;

/// <summary>Request body for <c>POST /api/metrics/catalog/dashboards</c>.</summary>
public sealed record MetricDashboardUsageRequest
{
    public string? MetricName { get; init; }
}

/// <summary>One panel that reads the metric, and how.</summary>
public sealed record MetricDashboardPanelUsage
{
    public required string PanelId { get; init; }

    public required string Title { get; init; }

    /// <summary>True when the metric is one of a Formula panel's queries rather than the panel's single selected metric.</summary>
    public required bool InFormula { get; init; }
}

/// <summary>One dashboard with at least one panel that reads the metric.</summary>
public sealed record MetricDashboardUsage
{
    public required Guid DashboardId { get; init; }

    public required string DashboardName { get; init; }

    public required IReadOnlyList<MetricDashboardPanelUsage> Panels { get; init; }
}

/// <summary>Response body for <c>POST /api/metrics/catalog/dashboards</c>, ordered by dashboard name.</summary>
public sealed record MetricDashboardUsageResponse
{
    public required IReadOnlyList<MetricDashboardUsage> Dashboards { get; init; }
}
