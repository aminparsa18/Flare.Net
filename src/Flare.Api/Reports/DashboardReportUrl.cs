using Flare.Api.Model;

namespace Flare.Api.Reports;

/// <summary>Builds the dashboard URL a report is rendered from.</summary>
public static class DashboardReportUrl
{
    /// <summary>
    /// <c>{baseUrl}/dashboards/{id}?report=1&amp;range=..&amp;var-..</c>. <c>report=1</c> makes the dashboard
    /// drop its chrome and load every panel eagerly; the range and variables use the dashboard's own URL state.
    /// </summary>
    public static string Build(string baseUrl, DashboardSchedule schedule)
    {
        var query = new List<string> { "report=1" };
        if (schedule.TimeRange.Length > 0)
        {
            query.Add("range=" + Uri.EscapeDataString(schedule.TimeRange));
        }

        if (schedule.VariableQuery.Length > 0)
        {
            query.Add(schedule.VariableQuery.TrimStart('?'));
        }

        return $"{baseUrl.TrimEnd('/')}/dashboards/{schedule.DashboardId}?{string.Join('&', query)}";
    }

    /// <summary>The link a report email points to: the same URL without <c>report=1</c>.</summary>
    public static string BuildViewLink(string baseUrl, DashboardSchedule schedule) =>
        Build(baseUrl, schedule).Replace("?report=1&", "?", StringComparison.Ordinal).Replace("?report=1", "", StringComparison.Ordinal);
}
