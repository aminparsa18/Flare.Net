using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Identity.Auth;

namespace Flare.Api.Endpoints;

/// <summary>The dashboards API: named, multi-panel dashboards composed from arbitrary log/trace/metric queries, under <c>/api/dashboards</c>.</summary>
/// <remarks>
/// Mirrors <see cref="SavedViewEndpoints"/>'s shape: POST/PUT + JSON body for anything
/// with a structured payload, manual <see cref="JsonSerializer.DeserializeAsync"/> against
/// the source-gen <see cref="DashboardsJsonContext"/>, <see cref="Results.Problem"/> on
/// 400s. No page-type query-string filter on the list endpoint - unlike a
/// <see cref="SavedView"/>, a dashboard isn't scoped to one Explorer page (see
/// <c>docs-internal/adr/0023-custom-dashboards.md</c>).
///
/// Unlike every other feature mapped wholesale onto one of Program.cs's three
/// authenticated/member/admin route groups, this file splits per-route: the create/
/// update/delete handlers below additionally require <see cref="AuthorizationPolicies.RequireMember"/>
/// (Admin or Member - Viewer stays read-only, mirroring the alert-rule/notification-channel
/// convention), while list/get stay on Program.cs's group-level plain <c>RequireAuthorization()</c>
/// (any authenticated user, Viewer included) - a dashboard is visible to everyone but only
/// Admin/Member can mutate it. ASP.NET Core's authorization metadata is additive, so adding
/// a per-route policy here on top of the group's own doesn't replace it, it ANDs with it.
/// The dashboard viewer/list UI mirrors this with `AuthState.canMutate` so a Viewer never
/// sees a control that would just 403 - see that property's own remarks.
/// </remarks>
public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/dashboards", HandleCreateAsync).RequireAuthorization(AuthorizationPolicies.RequireMember);
        endpoints.MapGet("/api/dashboards", HandleListAsync);
        endpoints.MapGet("/api/dashboards/{id:guid}", HandleGetAsync);
        endpoints.MapPut("/api/dashboards/{id:guid}", HandleUpdateAsync).RequireAuthorization(AuthorizationPolicies.RequireMember);
        endpoints.MapDelete("/api/dashboards/{id:guid}", HandleDeleteAsync).RequireAuthorization(AuthorizationPolicies.RequireMember);
        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, IDashboardQueryService dashboards, CancellationToken cancellationToken)
    {
        DashboardRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, DashboardsJsonContext.Default.DashboardRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var dashboard = await dashboards.CreateAsync(request, cancellationToken);
        return ApiSerialization.Write(http, dashboard, DashboardsJsonContext.Default.Dashboard, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAsync(HttpContext http, IDashboardQueryService dashboards, CancellationToken cancellationToken)
    {
        var list = await dashboards.ListAsync(cancellationToken);
        return ApiSerialization.Write(http, new DashboardListResponse { Dashboards = list }, DashboardsJsonContext.Default.DashboardListResponse);
    }

    private static async Task<IResult> HandleGetAsync(Guid id, HttpContext http, IDashboardQueryService dashboards, CancellationToken cancellationToken)
    {
        var dashboard = await dashboards.GetAsync(id, cancellationToken);
        return dashboard is null ? Results.NotFound() : ApiSerialization.Write(http, dashboard, DashboardsJsonContext.Default.Dashboard);
    }

    private static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, IDashboardQueryService dashboards, CancellationToken cancellationToken)
    {
        DashboardRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, DashboardsJsonContext.Default.DashboardRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var dashboard = await dashboards.UpdateAsync(id, request, cancellationToken);
        return dashboard is null ? Results.NotFound() : ApiSerialization.Write(http, dashboard, DashboardsJsonContext.Default.Dashboard);
    }

    private static async Task<IResult> HandleDeleteAsync(Guid id, IDashboardQueryService dashboards, CancellationToken cancellationToken)
    {
        var deleted = await dashboards.DeleteAsync(id, cancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
