namespace Flare.Api.Query;

/// <summary>
/// Instant, config-only rollback valve for the flush-time-pre-aggregated
/// <c>service_metrics</c> path (see ADR-0030) - same shape/purpose as
/// <see cref="Caching.QueryCacheOptions.Enabled"/> and ADR-0007's
/// <c>LogPatternOptions.Enabled</c>. <c>false</c> makes
/// <see cref="ServiceOverviewQueryService"/> always use the live <c>spans</c>
/// <see cref="ServiceOverviewQueryBuilder"/> path, exactly as if a resource-attribute
/// filter were always present, no redeploy or migration rollback needed.
/// </summary>
public sealed class ServiceMetricsOptions
{
    public const string SectionName = "ServiceMetrics";

    public bool Enabled { get; set; } = true;
}
