using System.Security.Claims;
using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Identity.Auth;
using Flare.Identity.Users;

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
///
/// On top of the Viewer/Member split above, update/delete are further narrowed to the
/// dashboard's own <see cref="Dashboard.OwnerUserId"/> - a Member who isn't the owner (and
/// isn't an Admin) gets a 403 too, same shape as <see cref="PersonalAccessTokenEndpoints"/>'
/// revoke check. See <see cref="CanMutate"/> and
/// <c>docs-internal/adr/0027-dashboard-ownership.md</c> for the full rationale, including
/// why list/get (visibility) are deliberately untouched by this - every dashboard stays
/// visible to everyone, only *who may change a given one* narrows. The dashboard
/// viewer/table UI mirrors this with `AuthState.canMutateDashboard`, same "UI-only, the API
/// enforces it independently" caveat `canMutate`'s own remarks give.
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

    internal static async Task<IResult> HandleCreateAsync(HttpContext http, ClaimsPrincipal principal, IDashboardQueryService dashboards, CancellationToken cancellationToken)
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

        // Whoever's authenticated when RequireMember lets this request through becomes the
        // owner; null (not a 401) when auth is disabled entirely - TryGetCurrentUserId
        // returning false there is expected, not an error, same as PersonalAccessTokenEndpoints'
        // own TryGetCurrentUserId remarks explain.
        var ownerUserId = TryGetCurrentUserId(principal, out var userId) ? userId : (Guid?)null;
        var dashboard = await dashboards.CreateAsync(request, ownerUserId, cancellationToken);
        return ApiSerialization.Write(http, dashboard, DashboardsJsonContext.Default.Dashboard, statusCode: StatusCodes.Status201Created);
    }

    internal static async Task<IResult> HandleListAsync(HttpContext http, IDashboardQueryService dashboards, CancellationToken cancellationToken)
    {
        var list = await dashboards.ListAsync(cancellationToken);
        return ApiSerialization.Write(http, new DashboardListResponse { Dashboards = list }, DashboardsJsonContext.Default.DashboardListResponse);
    }

    internal static async Task<IResult> HandleGetAsync(Guid id, HttpContext http, IDashboardQueryService dashboards, CancellationToken cancellationToken)
    {
        var dashboard = await dashboards.GetAsync(id, cancellationToken);
        return dashboard is null ? Results.NotFound() : ApiSerialization.Write(http, dashboard, DashboardsJsonContext.Default.Dashboard);
    }

    internal static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, ClaimsPrincipal principal, IDashboardQueryService dashboards, CancellationToken cancellationToken)
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

        var existing = await dashboards.GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        if (!CanMutate(existing, principal))
        {
            return Results.Forbid();
        }

        var dashboard = await dashboards.UpdateAsync(id, request, cancellationToken);
        return dashboard is null ? Results.NotFound() : ApiSerialization.Write(http, dashboard, DashboardsJsonContext.Default.Dashboard);
    }

    internal static async Task<IResult> HandleDeleteAsync(Guid id, ClaimsPrincipal principal, IDashboardQueryService dashboards, CancellationToken cancellationToken)
    {
        var existing = await dashboards.GetAsync(id, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        if (!CanMutate(existing, principal))
        {
            return Results.Forbid();
        }

        var deleted = await dashboards.DeleteAsync(id, cancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }

    /// <summary>
    /// Whether <paramref name="principal"/> may update/delete <paramref name="dashboard"/> -
    /// true for an unowned dashboard (see <see cref="Dashboard.OwnerUserId"/>'s remarks), the
    /// dashboard's own owner, or an Admin. <see cref="AuthorizationPolicies.RequireMember"/>
    /// on the route has already ruled out a Viewer by the time this runs.
    /// </summary>
    private static bool CanMutate(Dashboard dashboard, ClaimsPrincipal principal)
    {
        if (dashboard.OwnerUserId is not { } ownerId)
        {
            return true;
        }

        // No resolvable identity (Flare's opt-in auth is off, so RequireMember let this
        // through unauthenticated) - nothing to compare ownership against, so it can't be
        // the reason to refuse. Matches TryGetCurrentUserId's own "only meaningful with
        // auth enabled" remarks.
        if (!TryGetCurrentUserId(principal, out var userId))
        {
            return true;
        }

        return ownerId == userId || principal.IsInRole(nameof(UserRole.Admin));
    }

    /// <summary>Same pattern as <see cref="PersonalAccessTokenEndpoints"/>'s identically-named
    /// helper - see that one's remarks for why this only ever resolves a real id when
    /// Flare's opt-in auth is enabled.</summary>
    private static bool TryGetCurrentUserId(ClaimsPrincipal principal, out Guid userId)
    {
        userId = Guid.Empty;
        if (principal.Identity is not { IsAuthenticated: true })
        {
            return false;
        }

        var idClaim = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        return idClaim is not null && Guid.TryParse(idClaim, out userId);
    }
}
