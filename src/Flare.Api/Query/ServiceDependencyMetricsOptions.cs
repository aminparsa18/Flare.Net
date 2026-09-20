namespace Flare.Api.Query;

/// <summary>
/// Instant, config-only rollback valve for the flush-time-pre-aggregated Map-view
/// node stats (<c>service_dependency_nodes</c>) and per-node call-breakdown stats
/// (<c>service_call_breakdown_external</c>/<c>service_call_breakdown_database</c>) -
/// see ADR-0031. Same shape/purpose as <see cref="ServiceMetricsOptions.Enabled"/>,
/// one shared valve for both read paths rather than two since they ship in the same
/// migration. <c>false</c> makes <see cref="ServiceDependencyQueryService"/>'s nodes
/// query and <see cref="ServiceCallBreakdownQueryService"/>'s external/database
/// queries always use their live <c>spans</c> builders, exactly as if a
/// resource-attribute filter were always present, no redeploy or migration rollback
/// needed. Does not affect <see cref="ServiceDependencyQueryBuilder"/>'s edges query,
/// which has no pre-aggregated path (see ADR-0031's Context).
/// </summary>
public sealed class ServiceDependencyMetricsOptions
{
    public const string SectionName = "ServiceDependencyMetrics";

    public bool Enabled { get; set; } = true;
}
