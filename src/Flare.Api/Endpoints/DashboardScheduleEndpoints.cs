using System.Security.Claims;
using System.Text.Json;
using Flare.Api.Auditing;
using Flare.Api.Auth;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Identity.Users;

namespace Flare.Api.Endpoints;

/// <summary>
/// The scheduled dashboard report API: schedules under <c>/api/dashboards/{id}/schedules</c> and
/// <c>/api/dashboard-schedules/{id}</c>, plus "send now" and run history. Registered on the
/// Member/Admin-only group - a schedule makes this server email people. A caller needs access to the
/// dashboard's project, and changing or deleting a schedule needs being its creator or an Admin.
/// See <c>docs-internal/adr/0142-scheduled-dashboard-reports.md</c>.
/// </summary>
public static class DashboardScheduleEndpoints
{
    private const int RunHistoryLimit = 50;

    public static IEndpointRouteBuilder MapDashboardScheduleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/dashboards/{dashboardId:guid}/schedules", HandleListAsync);
        endpoints.MapPost("/api/dashboards/{dashboardId:guid}/schedules", HandleCreateAsync);
        endpoints.MapPut("/api/dashboard-schedules/{id:guid}", HandleUpdateAsync);
        endpoints.MapDelete("/api/dashboard-schedules/{id:guid}", HandleDeleteAsync);
        endpoints.MapPost("/api/dashboard-schedules/{id:guid}/send-now", HandleSendNowAsync);
        endpoints.MapGet("/api/dashboard-schedules/{id:guid}/runs", HandleRunsAsync);
        return endpoints;
    }

    internal static async Task<IResult> HandleListAsync(Guid dashboardId, HttpContext http, IDashboardQueryService dashboards, IDashboardScheduleQueryService schedules, CancellationToken cancellationToken)
    {
        if (await FindDashboardAsync(dashboardId, http, dashboards, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        var list = await schedules.ListForDashboardAsync(dashboardId, cancellationToken);
        return Results.Json(new DashboardScheduleListResponse(list), DashboardSchedulesJsonContext.Default.DashboardScheduleListResponse);
    }

    internal static async Task<IResult> HandleCreateAsync(Guid dashboardId, HttpContext http, ClaimsPrincipal principal, IDashboardQueryService dashboards, IDashboardScheduleQueryService schedules, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadRequestAsync(http, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        if (await FindDashboardAsync(dashboardId, http, dashboards, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        var schedule = await schedules.CreateAsync(dashboardId, request!, TryGetCurrentUserId(principal), cancellationToken);
        AuditContext.SetResourceId(http, schedule.Id);
        return Results.Json(schedule, DashboardSchedulesJsonContext.Default.DashboardSchedule, statusCode: StatusCodes.Status201Created);
    }

    internal static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, ClaimsPrincipal principal, IDashboardQueryService dashboards, IDashboardScheduleQueryService schedules, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadRequestAsync(http, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var existing = await schedules.GetAsync(id, cancellationToken);
        if (existing is null || await FindDashboardAsync(existing.DashboardId, http, dashboards, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        if (!CanMutate(existing, principal))
        {
            return Results.Forbid();
        }

        var updated = await schedules.UpdateAsync(id, request!, cancellationToken);
        if (updated is null)
        {
            return Results.NotFound();
        }

        AuditContext.SetChange(http, DashboardSchedulesJsonContext.Default.DashboardSchedule, existing, updated);
        return Results.Json(updated, DashboardSchedulesJsonContext.Default.DashboardSchedule);
    }

    internal static async Task<IResult> HandleDeleteAsync(Guid id, HttpContext http, ClaimsPrincipal principal, IDashboardQueryService dashboards, IDashboardScheduleQueryService schedules, CancellationToken cancellationToken)
    {
        var existing = await schedules.GetAsync(id, cancellationToken);
        if (existing is null || await FindDashboardAsync(existing.DashboardId, http, dashboards, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        if (!CanMutate(existing, principal))
        {
            return Results.Forbid();
        }

        return await schedules.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();
    }

    /// <summary>Marks the schedule due now; the worker picks it up on its next tick (within a minute) and records the run.</summary>
    internal static async Task<IResult> HandleSendNowAsync(Guid id, HttpContext http, ClaimsPrincipal principal, IDashboardQueryService dashboards, IDashboardScheduleQueryService schedules, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var existing = await schedules.GetAsync(id, cancellationToken);
        if (existing is null || await FindDashboardAsync(existing.DashboardId, http, dashboards, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        if (!CanMutate(existing, principal))
        {
            return Results.Forbid();
        }

        var updated = await schedules.SetNextRunAsync(existing with { Enabled = true }, timeProvider.GetUtcNow(), cancellationToken);
        return Results.Json(updated, DashboardSchedulesJsonContext.Default.DashboardSchedule, statusCode: StatusCodes.Status202Accepted);
    }

    internal static async Task<IResult> HandleRunsAsync(Guid id, HttpContext http, IDashboardQueryService dashboards, IDashboardScheduleQueryService schedules, CancellationToken cancellationToken)
    {
        var existing = await schedules.GetAsync(id, cancellationToken);
        if (existing is null || await FindDashboardAsync(existing.DashboardId, http, dashboards, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        var runs = await schedules.ListRunsAsync(id, RunHistoryLimit, cancellationToken);
        return Results.Json(new DashboardReportRunListResponse(runs), DashboardSchedulesJsonContext.Default.DashboardReportRunListResponse);
    }

    /// <summary>The dashboard, or null when it doesn't exist or the caller cannot write in its project.</summary>
    private static async Task<Dashboard?> FindDashboardAsync(Guid dashboardId, HttpContext http, IDashboardQueryService dashboards, CancellationToken cancellationToken)
    {
        var dashboard = await dashboards.GetAsync(dashboardId, cancellationToken);
        var access = http.GetProjectAccess();
        return dashboard is not null && access.CanRead(dashboard.ProjectId) && access.CanWrite(dashboard.ProjectId) ? dashboard : null;
    }

    /// <summary>True for the creator, an Admin, or a schedule created while auth was off.</summary>
    private static bool CanMutate(DashboardSchedule schedule, ClaimsPrincipal principal)
    {
        if (schedule.OwnerUserId is not { } ownerId || TryGetCurrentUserId(principal) is not { } userId)
        {
            return true;
        }

        return ownerId == userId || principal.IsInRole(nameof(UserRole.Admin));
    }

    private static Guid? TryGetCurrentUserId(ClaimsPrincipal principal) =>
        principal.Identity is { IsAuthenticated: true } && Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) ? userId : null;

    private static async Task<(DashboardScheduleRequest? Request, IResult? Problem)> ReadRequestAsync(HttpContext http, CancellationToken cancellationToken)
    {
        DashboardScheduleRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, DashboardSchedulesJsonContext.Default.DashboardScheduleRequest, cancellationToken);
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
