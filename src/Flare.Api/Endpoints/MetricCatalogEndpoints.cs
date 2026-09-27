using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The Metrics catalog page: <c>POST /api/metrics/catalog</c> (every metric in a window with
/// its series count, sample volume, and last-received time) and
/// <c>POST /api/metrics/catalog/detail</c> (one metric's services, per-attribute cardinality,
/// and related metrics) - see <see cref="MetricCatalogQueryBuilder"/>.
/// </summary>
/// <remarks>POST + JSON body, same rationale as <see cref="MetricsEndpoints"/>.</remarks>
public static class MetricCatalogEndpoints
{
    public static IEndpointRouteBuilder MapMetricCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/metrics/catalog", HandleListAsync);
        endpoints.MapPost("/api/metrics/catalog/detail", HandleDetailAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleListAsync(
        HttpContext http,
        IMetricCatalogQueryService queryService,
        CancellationToken cancellationToken)
    {
        MetricCatalogRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, MetricsJsonContext.Default.MetricCatalogRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.ListAsync(request ?? new MetricCatalogRequest(), cancellationToken);
        return ApiSerialization.Write(http, response, MetricsJsonContext.Default.MetricCatalogResponse);
    }

    private static async Task<IResult> HandleDetailAsync(
        HttpContext http,
        IMetricCatalogQueryService queryService,
        CancellationToken cancellationToken)
    {
        MetricCatalogDetailRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, MetricsJsonContext.Default.MetricCatalogDetailRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (string.IsNullOrWhiteSpace(request?.MetricName))
        {
            return Results.Problem("metricName is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetDetailAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, MetricsJsonContext.Default.MetricCatalogDetailResponse);
    }
}
