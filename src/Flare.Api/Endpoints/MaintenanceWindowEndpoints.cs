using System.Text.Json;
using Flare.Api.Alerting;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The maintenance-window API: CRUD under <c>/api/maintenance-windows</c> - same shape as
/// <see cref="NotificationChannelEndpoints"/>. See
/// <c>docs-internal/adr/0055-alert-maintenance-windows.md</c>.
/// </summary>
public static class MaintenanceWindowEndpoints
{
    public static IEndpointRouteBuilder MapMaintenanceWindowEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/maintenance-windows", HandleCreateAsync);
        endpoints.MapGet("/api/maintenance-windows", HandleListAsync);
        endpoints.MapGet("/api/maintenance-windows/{id:guid}", HandleGetAsync);
        endpoints.MapPut("/api/maintenance-windows/{id:guid}", HandleUpdateAsync);
        endpoints.MapDelete("/api/maintenance-windows/{id:guid}", HandleDeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, IMaintenanceWindowQueryService windows, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadRequestAsync(http, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var window = await windows.CreateAsync(request!, cancellationToken);
        return ApiSerialization.Write(http, window, MaintenanceWindowsJsonContext.Default.MaintenanceWindow, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAsync(HttpContext http, IMaintenanceWindowQueryService windows, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var list = await windows.ListAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        var response = new MaintenanceWindowListResponse
        {
            Windows = list,
            ActiveWindowIds = list.Where(w => MaintenanceWindowSchedule.IsActive(w, now)).Select(w => w.Id).ToList(),
        };
        return ApiSerialization.Write(http, response, MaintenanceWindowsJsonContext.Default.MaintenanceWindowListResponse);
    }

    private static async Task<IResult> HandleGetAsync(Guid id, HttpContext http, IMaintenanceWindowQueryService windows, CancellationToken cancellationToken)
    {
        var window = await windows.GetAsync(id, cancellationToken);
        return window is null ? Results.NotFound() : ApiSerialization.Write(http, window, MaintenanceWindowsJsonContext.Default.MaintenanceWindow);
    }

    private static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, IMaintenanceWindowQueryService windows, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadRequestAsync(http, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var window = await windows.UpdateAsync(id, request!, cancellationToken);
        return window is null ? Results.NotFound() : ApiSerialization.Write(http, window, MaintenanceWindowsJsonContext.Default.MaintenanceWindow);
    }

    private static async Task<IResult> HandleDeleteAsync(Guid id, IMaintenanceWindowQueryService windows, CancellationToken cancellationToken)
    {
        var deleted = await windows.DeleteAsync(id, cancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<(MaintenanceWindowRequest? Request, IResult? Problem)> ReadRequestAsync(HttpContext http, CancellationToken cancellationToken)
    {
        MaintenanceWindowRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, MaintenanceWindowsJsonContext.Default.MaintenanceWindowRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return (null, Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }

        if (request is null)
        {
            return (null, Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest));
        }

        if (request.Validate() is { } error)
        {
            return (null, Results.Problem(error, statusCode: StatusCodes.Status400BadRequest));
        }

        return (request, null);
    }
}
