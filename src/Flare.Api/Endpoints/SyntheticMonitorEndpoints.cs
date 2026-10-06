using System.Text.Json;
using Flare.Api.Auditing;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The synthetic monitor API: CRUD under <c>/api/synthetic-monitors</c> - same shape as
/// <see cref="OnCallRotationEndpoints"/>. See <c>docs-internal/adr/0128-synthetic-monitoring.md</c>.
/// </summary>
public static class SyntheticMonitorEndpoints
{
    public static IEndpointRouteBuilder MapSyntheticMonitorEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/synthetic-monitors", HandleCreateAsync);
        endpoints.MapGet("/api/synthetic-monitors", HandleListAsync);
        endpoints.MapGet("/api/synthetic-monitors/{id:guid}", HandleGetAsync);
        endpoints.MapPut("/api/synthetic-monitors/{id:guid}", HandleUpdateAsync);
        endpoints.MapDelete("/api/synthetic-monitors/{id:guid}", HandleDeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, ISyntheticMonitorQueryService monitors, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadRequestAsync(http, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var monitor = await monitors.CreateAsync(request!, cancellationToken);
        AuditContext.SetResourceId(http, monitor.Id);
        return Results.Json(monitor, SyntheticMonitorsJsonContext.Default.SyntheticMonitor, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAsync(ISyntheticMonitorQueryService monitors, CancellationToken cancellationToken)
    {
        var list = await monitors.ListAsync(cancellationToken);
        var statuses = await monitors.LatestStatusesAsync(cancellationToken);
        var withStatus = list.Select(m => statuses.TryGetValue(m.Name, out var byLocation)
            ? m with { Latest = byLocation.Select(l => l.Status).MaxBy(s => s.Time), LocationStatuses = byLocation }
            : m).ToList();
        return Results.Json(new SyntheticMonitorListResponse(withStatus), SyntheticMonitorsJsonContext.Default.SyntheticMonitorListResponse);
    }

    private static async Task<IResult> HandleGetAsync(Guid id, ISyntheticMonitorQueryService monitors, CancellationToken cancellationToken)
    {
        var monitor = await monitors.GetAsync(id, cancellationToken);
        return monitor is null ? Results.NotFound() : Results.Json(monitor, SyntheticMonitorsJsonContext.Default.SyntheticMonitor);
    }

    private static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, ISyntheticMonitorQueryService monitors, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadRequestAsync(http, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var before = await monitors.GetAsync(id, cancellationToken);
        var monitor = await monitors.UpdateAsync(id, request!, cancellationToken);
        if (monitor is null)
        {
            return Results.NotFound();
        }

        AuditContext.SetChange(http, SyntheticMonitorsJsonContext.Default.SyntheticMonitor, before, monitor);
        return Results.Json(monitor, SyntheticMonitorsJsonContext.Default.SyntheticMonitor);
    }

    private static async Task<IResult> HandleDeleteAsync(Guid id, ISyntheticMonitorQueryService monitors, CancellationToken cancellationToken) =>
        await monitors.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();

    private static async Task<(SyntheticMonitorRequest? Request, IResult? Problem)> ReadRequestAsync(HttpContext http, CancellationToken cancellationToken)
    {
        SyntheticMonitorRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, SyntheticMonitorsJsonContext.Default.SyntheticMonitorRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return (null, Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }

        if (request is null)
        {
            return (null, Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest));
        }

        return request.Validate() is { } error
            ? (null, Results.Problem(error, statusCode: StatusCodes.Status400BadRequest))
            : (request, null);
    }
}
