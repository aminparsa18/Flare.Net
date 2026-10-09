using System.Text.Json;
using Flare.Api.Auditing;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Api.Status;

namespace Flare.Api.Endpoints;

/// <summary>
/// Status pages (ADR-0158): admin CRUD under <c>/api/status-pages</c>, and the one unauthenticated read,
/// <c>GET /api/public/status/{slug}</c>. Publishing is Admin-only because it exposes health data to anyone
/// with the URL; a page is off until someone enables it.
/// </summary>
public static class StatusPageEndpoints
{
    public static IEndpointRouteBuilder MapStatusPageAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/status-pages", HandleCreateAsync);
        endpoints.MapGet("/api/status-pages", HandleListAsync);
        endpoints.MapGet("/api/status-pages/{id:guid}", HandleGetAsync);
        endpoints.MapPut("/api/status-pages/{id:guid}", HandleUpdateAsync);
        endpoints.MapDelete("/api/status-pages/{id:guid}", HandleDeleteAsync);
        return endpoints;
    }

    /// <summary>Mapped outside the authenticated groups: the page being enabled is the only credential.</summary>
    public static IEndpointRouteBuilder MapPublicStatusEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/public/status/{slug}", HandlePublicAsync);
        return endpoints;
    }

    private static async Task<IResult> HandlePublicAsync(string slug, HttpContext http, IPublicStatusService status, CancellationToken cancellationToken)
    {
        var page = StatusPageRequest.IsValidSlug(slug) ? await status.GetAsync(slug, cancellationToken) : null;
        if (page is null)
        {
            return Results.NotFound();
        }

        http.Response.Headers.CacheControl = $"public, max-age={(int)PublicStatusService.CacheTtl.TotalSeconds}";
        return Results.Json(page, StatusPagesJsonContext.Default.PublicStatusPage);
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, IStatusPageQueryService pages, ISyntheticMonitorQueryService monitors, ISloQueryService slos, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadRequestAsync(http, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        if (await CheckAsync(request!, null, pages, monitors, slos, cancellationToken) is { } rejected)
        {
            return rejected;
        }

        var page = await pages.CreateAsync(request!, cancellationToken);
        AuditContext.SetResourceId(http, page.Id);
        return Results.Json(page, StatusPagesJsonContext.Default.StatusPage, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAsync(IStatusPageQueryService pages, CancellationToken cancellationToken) =>
        Results.Json(new StatusPageListResponse(await pages.ListAsync(cancellationToken)), StatusPagesJsonContext.Default.StatusPageListResponse);

    private static async Task<IResult> HandleGetAsync(Guid id, IStatusPageQueryService pages, CancellationToken cancellationToken)
    {
        var page = await pages.GetAsync(id, cancellationToken);
        return page is null ? Results.NotFound() : Results.Json(page, StatusPagesJsonContext.Default.StatusPage);
    }

    private static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, IStatusPageQueryService pages, ISyntheticMonitorQueryService monitors, ISloQueryService slos, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadRequestAsync(http, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        if (await CheckAsync(request!, id, pages, monitors, slos, cancellationToken) is { } rejected)
        {
            return rejected;
        }

        var before = await pages.GetAsync(id, cancellationToken);
        var page = await pages.UpdateAsync(id, request!, cancellationToken);
        if (page is not null)
        {
            AuditContext.SetChange(http, StatusPagesJsonContext.Default.StatusPage, before, page);
        }

        return page is null ? Results.NotFound() : Results.Json(page, StatusPagesJsonContext.Default.StatusPage);
    }

    private static async Task<IResult> HandleDeleteAsync(Guid id, IStatusPageQueryService pages, CancellationToken cancellationToken) =>
        await pages.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();

    /// <summary>A 409 for a taken slug, a 400 for a component that points at nothing; null when the request can be saved.</summary>
    private static async Task<IResult?> CheckAsync(
        StatusPageRequest request,
        Guid? exceptId,
        IStatusPageQueryService pages,
        ISyntheticMonitorQueryService monitors,
        ISloQueryService slos,
        CancellationToken cancellationToken)
    {
        var slug = request.Slug.Trim();
        if ((await pages.ListAsync(cancellationToken)).Any(p => p.Id != exceptId && string.Equals(p.Slug, slug, StringComparison.Ordinal)))
        {
            return Results.Problem($"A status page with slug '{slug}' already exists.", statusCode: StatusCodes.Status409Conflict);
        }

        var components = request.Components ?? [];
        var monitorIds = components.Any(c => c.Kind == StatusComponentKind.Monitor)
            ? (await monitors.ListAsync(cancellationToken)).Select(m => m.Id).ToHashSet()
            : [];
        var sloIds = components.Any(c => c.Kind == StatusComponentKind.Slo)
            ? (await slos.ListAsync(cancellationToken)).Select(s => s.Id).ToHashSet()
            : [];
        foreach (var component in components)
        {
            var exists = component.Kind == StatusComponentKind.Monitor ? monitorIds.Contains(component.RefId) : sloIds.Contains(component.RefId);
            if (!exists)
            {
                return Results.Problem($"Component '{component.Name}' points at a {component.Kind.ToString().ToLowerInvariant()} that does not exist.", statusCode: StatusCodes.Status400BadRequest);
            }
        }

        return null;
    }

    private static async Task<(StatusPageRequest? Request, IResult? Problem)> ReadRequestAsync(HttpContext http, CancellationToken cancellationToken)
    {
        StatusPageRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, StatusPagesJsonContext.Default.StatusPageRequest, cancellationToken);
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
