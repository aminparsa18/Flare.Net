using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Identity.MetricMetadata;

namespace Flare.Api.Endpoints;

/// <summary>
/// Admin overrides for a metric's unit and description - see
/// docs-internal/adr/0065-metric-metadata-overrides.md. Both routes are Admin-only
/// (<c>Program.cs</c>'s <c>adminRoutes</c>), same "mutating a global, cross-user setting"
/// reasoning as <see cref="ApdexThresholdEndpoints"/>. There's no read route: the catalog and
/// <c>/api/metrics/names</c> already return the overridden values.
/// </summary>
/// <remarks>
/// The metric name travels in the body/query string rather than the path - the OTel
/// instrument-name syntax allows <c>/</c>, which a route segment can't carry.
/// </remarks>
public static class MetricMetadataOverrideEndpoints
{
    public static IEndpointRouteBuilder MapMetricMetadataOverrideEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/metrics/metadata-overrides", HandleSetAsync);
        endpoints.MapDelete("/api/metrics/metadata-overrides", HandleResetAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleSetAsync(
        HttpContext http,
        IMetricMetadataOverrideStore store,
        CancellationToken cancellationToken)
    {
        SetMetricMetadataOverrideRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, MetricsJsonContext.Default.SetMetricMetadataOverrideRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("A request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (MetricMetadataOverlay.Validate(request, out var normalized) is { } error)
        {
            return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        await store.SetAsync(normalized, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> HandleResetAsync(
        string? metricName,
        IMetricMetadataOverrideStore store,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(metricName))
        {
            return Results.Problem("metricName is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        await store.ResetAsync(metricName.Trim(), cancellationToken);
        return Results.NoContent();
    }
}
