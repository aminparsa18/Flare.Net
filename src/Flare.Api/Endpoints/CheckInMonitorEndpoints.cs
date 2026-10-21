using System.Text.Json;
using Flare.Api.Auditing;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The check-in monitor API: CRUD under <c>/api/checkin-monitors</c> - same shape as <see cref="SyntheticMonitorEndpoints"/>.
/// See <c>docs-internal/adr/0260-cron-job-checkins.md</c>.
/// </summary>
public static class CheckInMonitorEndpoints
{
    private const int DefaultRunLimit = 50;
    private const int MaxRunLimit = 200;

    /// <summary>List and get, open to every signed-in user: a monitor only reports whether a job is on schedule.</summary>
    public static IEndpointRouteBuilder MapCheckInMonitorReadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/checkin-monitors", HandleListAsync);
        endpoints.MapGet("/api/checkin-monitors/{id:guid}", HandleGetAsync);
        endpoints.MapGet("/api/checkin-monitors/{id:guid}/runs", HandleRunsAsync);
        return endpoints;
    }

    /// <summary>Create, update and delete, Member or Admin.</summary>
    public static IEndpointRouteBuilder MapCheckInMonitorWriteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/checkin-monitors", HandleCreateAsync);
        endpoints.MapPut("/api/checkin-monitors/{id:guid}", HandleUpdateAsync);
        endpoints.MapDelete("/api/checkin-monitors/{id:guid}", HandleDeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, ICheckInMonitorQueryService monitors, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadRequestAsync(http, creating: true, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var monitor = await monitors.CreateAsync(request!, cancellationToken);
        if (monitor is null)
        {
            return Results.Problem($"A check-in monitor with the slug '{request!.EffectiveSlug()}' already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        AuditContext.SetResourceId(http, monitor.Id);
        return Results.Json(monitor, CheckInMonitorsJsonContext.Default.CheckInMonitor, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAsync(ICheckInMonitorQueryService monitors, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var list = await monitors.ListAsync(cancellationToken);
        var pings = await monitors.PingsAsync(list.Select(m => m.Slug).ToList(), cancellationToken);
        var now = timeProvider.GetUtcNow();
        var withStatus = list
            .Select(m => m with { Status = CheckInEvaluator.Evaluate(m, pings.GetValueOrDefault(m.Slug, CheckInPings.None), now) })
            .ToList();
        return Results.Json(new CheckInMonitorListResponse(withStatus), CheckInMonitorsJsonContext.Default.CheckInMonitorListResponse);
    }

    private static async Task<IResult> HandleGetAsync(Guid id, ICheckInMonitorQueryService monitors, CancellationToken cancellationToken)
    {
        var monitor = await monitors.GetAsync(id, cancellationToken);
        return monitor is null ? Results.NotFound() : Results.Json(monitor, CheckInMonitorsJsonContext.Default.CheckInMonitor);
    }

    /// <summary>The monitor's recent runs, newest first, paired from its pings. <c>limit</c> defaults to 50, at most 200.</summary>
    private static async Task<IResult> HandleRunsAsync(Guid id, int? limit, ICheckInMonitorQueryService monitors, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var monitor = await monitors.GetAsync(id, cancellationToken);
        if (monitor is null)
        {
            return Results.NotFound();
        }

        var take = Math.Clamp(limit ?? DefaultRunLimit, 1, MaxRunLimit);

        // A run is at most two pings, so this window holds the newest `take` runs; the oldest run in it may be cut off.
        var pings = await monitors.RecentPingsAsync(monitor.Slug, take * 2 + 1, cancellationToken);
        var runs = CheckInRunHistory.Build(pings, monitor.MaxRuntimeSeconds, timeProvider.GetUtcNow()).Take(take).ToList();
        return Results.Json(new CheckInRunListResponse(runs), CheckInMonitorsJsonContext.Default.CheckInRunListResponse);
    }

    private static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, ICheckInMonitorQueryService monitors, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadRequestAsync(http, creating: false, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var before = await monitors.GetAsync(id, cancellationToken);
        if (before is null)
        {
            return Results.NotFound();
        }

        var monitor = await monitors.UpdateAsync(id, request!, cancellationToken);
        if (monitor is null)
        {
            return Results.NotFound();
        }

        AuditContext.SetChange(http, CheckInMonitorsJsonContext.Default.CheckInMonitor, before, monitor);
        return Results.Json(monitor, CheckInMonitorsJsonContext.Default.CheckInMonitor);
    }

    private static async Task<IResult> HandleDeleteAsync(Guid id, ICheckInMonitorQueryService monitors, CancellationToken cancellationToken) =>
        await monitors.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();

    private static async Task<(CheckInMonitorRequest? Request, IResult? Problem)> ReadRequestAsync(HttpContext http, bool creating, CancellationToken cancellationToken)
    {
        CheckInMonitorRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, CheckInMonitorsJsonContext.Default.CheckInMonitorRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return (null, Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }

        if (request is null)
        {
            return (null, Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest));
        }

        return request.Validate(creating) is { } error
            ? (null, Results.Problem(error, statusCode: StatusCodes.Status400BadRequest))
            : (request, null);
    }
}
