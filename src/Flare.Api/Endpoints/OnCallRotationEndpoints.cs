using System.Text.Json;
using Flare.Api.Alerting;
using Flare.Api.Auditing;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The on-call rotation API: CRUD under <c>/api/oncall-rotations</c> - same shape as
/// <see cref="MaintenanceWindowEndpoints"/>. Reads include who is on call now. See
/// <c>docs-internal/adr/0126-alert-oncall-rotations.md</c>.
/// </summary>
public static class OnCallRotationEndpoints
{
    public static IEndpointRouteBuilder MapOnCallRotationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/oncall-rotations", HandleCreateAsync);
        endpoints.MapGet("/api/oncall-rotations", HandleListAsync);
        endpoints.MapGet("/api/oncall-rotations/{id:guid}", HandleGetAsync);
        endpoints.MapPut("/api/oncall-rotations/{id:guid}", HandleUpdateAsync);
        endpoints.MapDelete("/api/oncall-rotations/{id:guid}", HandleDeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, IOnCallRotationQueryService rotations, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadRequestAsync(http, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var rotation = await rotations.CreateAsync(request!, cancellationToken);
        AuditContext.SetResourceId(http, rotation.Id);
        return Results.Json(OnCallSchedule.Resolve(rotation, timeProvider.GetUtcNow()), OnCallRotationsJsonContext.Default.OnCallRotationStatus, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAsync(IOnCallRotationQueryService rotations, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var list = await rotations.ListAsync(cancellationToken);
        return Results.Json(new OnCallRotationListResponse([.. list.Select(r => OnCallSchedule.Resolve(r, now))]), OnCallRotationsJsonContext.Default.OnCallRotationListResponse);
    }

    private static async Task<IResult> HandleGetAsync(Guid id, IOnCallRotationQueryService rotations, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var rotation = await rotations.GetAsync(id, cancellationToken);
        return rotation is null ? Results.NotFound() : Results.Json(OnCallSchedule.Resolve(rotation, timeProvider.GetUtcNow()), OnCallRotationsJsonContext.Default.OnCallRotationStatus);
    }

    private static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, IOnCallRotationQueryService rotations, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadRequestAsync(http, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var before = await rotations.GetAsync(id, cancellationToken);
        var rotation = await rotations.UpdateAsync(id, request!, cancellationToken);
        if (rotation is null)
        {
            return Results.NotFound();
        }

        AuditContext.SetChange(http, OnCallRotationsJsonContext.Default.OnCallRotation, before, rotation);
        return Results.Json(OnCallSchedule.Resolve(rotation, timeProvider.GetUtcNow()), OnCallRotationsJsonContext.Default.OnCallRotationStatus);
    }

    private static async Task<IResult> HandleDeleteAsync(Guid id, IOnCallRotationQueryService rotations, CancellationToken cancellationToken) =>
        await rotations.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();

    private static async Task<(OnCallRotationRequest? Request, IResult? Problem)> ReadRequestAsync(HttpContext http, CancellationToken cancellationToken)
    {
        OnCallRotationRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, OnCallRotationsJsonContext.Default.OnCallRotationRequest, cancellationToken);
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
