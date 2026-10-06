using System.Text.Json;
using Flare.Api.Auditing;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>Log-based metric CRUD under <c>/api/log-metrics</c>. Same shape as <see cref="MetricAttributeRuleEndpoints"/>; see docs-internal/adr/0140-log-based-metrics.md.</summary>
public static class LogMetricEndpoints
{
    public static IEndpointRouteBuilder MapLogMetricEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/log-metrics", HandleCreateAsync);
        endpoints.MapPost("/api/log-metrics/preview", HandlePreviewAsync);
        endpoints.MapGet("/api/log-metrics", HandleListAsync);
        endpoints.MapGet("/api/log-metrics/{id:guid}", HandleGetAsync);
        endpoints.MapPut("/api/log-metrics/{id:guid}", HandleUpdateAsync);
        endpoints.MapDelete("/api/log-metrics/{id:guid}", HandleDeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, ILogMetricQueryService metrics, CancellationToken cancellationToken)
    {
        var (request, error) = await ReadRequestAsync(http, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var metric = await metrics.CreateAsync(request!, cancellationToken);
        AuditContext.SetResourceId(http, metric.Id);
        return ApiSerialization.Write(http, metric, LogMetricsJsonContext.Default.LogMetric, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandlePreviewAsync(HttpContext http, ILogMetricQueryService metrics, CancellationToken cancellationToken)
    {
        LogMetricPreviewRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, LogMetricsJsonContext.Default.LogMetricPreviewRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Validate() is { } validationError)
        {
            return Results.Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await metrics.PreviewAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, LogMetricsJsonContext.Default.LogMetricPreviewResponse);
    }

    private static async Task<IResult> HandleListAsync(HttpContext http, ILogMetricQueryService metrics, CancellationToken cancellationToken)
    {
        var list = await metrics.ListAsync(cancellationToken);
        return ApiSerialization.Write(http, new LogMetricListResponse { Metrics = list }, LogMetricsJsonContext.Default.LogMetricListResponse);
    }

    private static async Task<IResult> HandleGetAsync(Guid id, HttpContext http, ILogMetricQueryService metrics, CancellationToken cancellationToken)
    {
        var metric = await metrics.GetAsync(id, cancellationToken);
        return metric is null ? Results.NotFound() : ApiSerialization.Write(http, metric, LogMetricsJsonContext.Default.LogMetric);
    }

    private static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, ILogMetricQueryService metrics, CancellationToken cancellationToken)
    {
        var (request, error) = await ReadRequestAsync(http, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var before = await metrics.GetAsync(id, cancellationToken);
        var metric = await metrics.UpdateAsync(id, request!, cancellationToken);
        if (metric is null)
        {
            return Results.NotFound();
        }

        AuditContext.SetChange(http, LogMetricsJsonContext.Default.LogMetric, before, metric);
        return ApiSerialization.Write(http, metric, LogMetricsJsonContext.Default.LogMetric);
    }

    private static async Task<IResult> HandleDeleteAsync(Guid id, ILogMetricQueryService metrics, CancellationToken cancellationToken) =>
        await metrics.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();

    private static async Task<(LogMetricRequest? Request, IResult? Error)> ReadRequestAsync(HttpContext http, CancellationToken cancellationToken)
    {
        LogMetricRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, LogMetricsJsonContext.Default.LogMetricRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return (null, Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }

        if (request is null)
        {
            return (null, Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest));
        }

        return request.Validate() is { } validationError
            ? (null, Results.Problem(validationError, statusCode: StatusCodes.Status400BadRequest))
            : (request, null);
    }
}
