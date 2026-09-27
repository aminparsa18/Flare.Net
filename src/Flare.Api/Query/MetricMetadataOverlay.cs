using Flare.Api.Model;
using Flare.Identity.MetricMetadata;

namespace Flare.Api.Query;

/// <summary>
/// Applies admin metric-metadata overrides (<see cref="IMetricMetadataOverrideStore"/>,
/// ADR-0065) over the unit/description a metric was emitted with. Pure, so the merge rule is
/// unit-tested once and shared by the catalog and the metric picker.
/// </summary>
/// <remarks>
/// Applied per response after the ClickHouse read - never folded into SQL, and after
/// <see cref="Caching.CachingMetricQueryService"/> for <c>/api/metrics/names</c> - so an
/// override shows on the next request rather than after a cache entry expires. Each member
/// overrides independently: a null override member leaves the emitted value.
/// </remarks>
public static class MetricMetadataOverlay
{
    public const int MaxUnitLength = 64;
    public const int MaxDescriptionLength = 1000;

    public static (string? Unit, string? Description) Apply(MetricMetadataOverride? metadataOverride, string? emittedUnit, string? emittedDescription) =>
        metadataOverride is null
            ? (emittedUnit, emittedDescription)
            : (metadataOverride.Unit ?? emittedUnit, metadataOverride.Description ?? emittedDescription);

    public static MetricNamesResponse Apply(MetricNamesResponse response, IReadOnlyDictionary<string, MetricMetadataOverride> overrides)
    {
        if (overrides.Count == 0)
        {
            return response;
        }

        return response with
        {
            Metrics = response.Metrics
                .Select(m =>
                {
                    if (!overrides.TryGetValue(m.MetricName, out var o))
                    {
                        return m;
                    }

                    var (unit, description) = Apply(o, m.Unit, m.Description);
                    return m with { Unit = unit, Description = description };
                })
                .ToList(),
        };
    }

    /// <summary>
    /// Trims an incoming override: blank means "not overridden". Returns an error message when
    /// both are blank or either is too long, otherwise null.
    /// </summary>
    public static string? Validate(SetMetricMetadataOverrideRequest request, out MetricMetadataOverride normalized)
    {
        var unit = string.IsNullOrWhiteSpace(request.Unit) ? null : request.Unit.Trim();
        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        normalized = new MetricMetadataOverride(request.MetricName?.Trim() ?? "", unit, description);

        if (normalized.MetricName.Length == 0)
        {
            return "metricName is required.";
        }

        if (unit is null && description is null)
        {
            return "Set a unit or a description - to remove an override, DELETE it instead.";
        }

        if (unit?.Length > MaxUnitLength)
        {
            return $"unit must be at most {MaxUnitLength} characters.";
        }

        return description?.Length > MaxDescriptionLength
            ? $"description must be at most {MaxDescriptionLength} characters."
            : null;
    }
}
