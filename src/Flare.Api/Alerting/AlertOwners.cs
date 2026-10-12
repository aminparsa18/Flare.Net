using Flare.Api.Ai;

namespace Flare.Api.Alerting;

/// <summary>
/// The <c>{{owner}}</c> placeholder (ADR-0197): the distinct owners of an exception-count fire's top
/// error groups, in rank order, comma-separated. Groups nobody owns contribute nothing.
/// </summary>
public static class AlertOwners
{
    /// <summary>How many of the top groups are looked up for a notification.</summary>
    public const int GroupCount = 3;

    public static string Join(IEnumerable<IncidentExceptionGroup> groups) =>
        string.Join(", ", groups.Select(g => g.Owner).Where(o => o.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase));
}
