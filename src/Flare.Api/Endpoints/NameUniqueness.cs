namespace Flare.Api.Endpoints;

/// <summary>
/// Names of notification channels, alert rules, SLOs, pipeline rules, maintenance windows, metric attribute rules and (per project) dashboards are unique (case-insensitive), because declarative
/// tooling addresses them by name and the alert import already resolves references that way (ADR-0146).
/// Enforced on write rather than by a constraint: these live in ClickHouse, which has none, and rows that
/// predate the rule must stay editable, so an unchanged name is never rejected.
/// </summary>
internal static class NameUniqueness
{
    /// <summary>A 409 when <paramref name="name"/> belongs to another item; null when it's free or unchanged.</summary>
    public static IResult? Conflict(
        IEnumerable<(Guid Id, string Name)> existing, string kind, string? name, Guid? exceptId = null, string? currentName = null)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name) || string.Equals(name, currentName?.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return existing.Any(e => e.Id != exceptId && string.Equals(e.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))
            ? Results.Problem($"A {kind} named '{name}' already exists.", statusCode: StatusCodes.Status409Conflict)
            : null;
    }
}
