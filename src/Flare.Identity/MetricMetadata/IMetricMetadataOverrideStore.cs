namespace Flare.Identity.MetricMetadata;

/// <summary>An admin's replacement unit/description for one metric name. A null member isn't overridden.</summary>
public sealed record MetricMetadataOverride(string MetricName, string? Unit, string? Description);

/// <summary>
/// Admin overrides for a metric's unit and description, shown in place of what the
/// instrumentation sent - see docs-internal/adr/0065-metric-metadata-overrides.md. Only
/// overrides are stored; a metric with no row shows its emitted metadata.
/// </summary>
public interface IMetricMetadataOverrideStore
{
    /// <summary>
    /// Every override, keyed by <c>MetricName</c>. The whole map rather than a per-name lookup,
    /// same as <see cref="Apdex.IApdexThresholdStore.GetAllAsync"/>: the catalog and metric
    /// picker merge it into lists of metrics, and it's naturally small (one row per metric an
    /// admin has actually corrected).
    /// </summary>
    Task<IReadOnlyDictionary<string, MetricMetadataOverride>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Upserts the override for one metric, replacing both members.</summary>
    Task SetAsync(MetricMetadataOverride metadataOverride, CancellationToken cancellationToken = default);

    /// <summary>Removes a metric's override. A no-op if none exists.</summary>
    Task ResetAsync(string metricName, CancellationToken cancellationToken = default);
}
