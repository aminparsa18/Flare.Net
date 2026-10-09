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
        endpoints.MapGet("/api/status-pages/{id:guid}/incidents", HandleListIncidentsAsync);
        endpoints.MapPost("/api/status-pages/{id:guid}/incidents", HandleOpenIncidentAsync);
        endpoints.MapPost("/api/status-pages/{id:guid}/incidents/{incidentId:guid}/updates", HandleUpdateIncidentAsync);
        endpoints.MapDelete("/api/status-pages/{id:guid}/incidents/{incidentId:guid}", HandleDeleteIncidentAsync);
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

    private static async Task<IResult> HandleListIncidentsAsync(Guid id, IStatusPageQueryService pages, IStatusIncidentQueryService incidents, CancellationToken cancellationToken) =>
        await pages.GetAsync(id, cancellationToken) is null
            ? Results.NotFound()
            : Results.Json(new StatusIncidentListResponse(await incidents.ListAsync(id, cancellationToken)), StatusPagesJsonContext.Default.StatusIncidentListResponse);

    private static async Task<IResult> HandleOpenIncidentAsync(Guid id, HttpContext http, IStatusPageQueryService pages, IStatusIncidentQueryService incidents, TimeProvider time, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadBodyAsync(http, StatusPagesJsonContext.Default.StatusIncidentRequest, r => r.Validate(), cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var page = await pages.GetAsync(id, cancellationToken);
        if (page is null)
        {
            return Results.NotFound();
        }

        if (UnknownComponent(request!.Components, page) is { } unknown)
        {
            return unknown;
        }

        var incident = StatusIncidents.Open(id, request!, time.GetUtcNow());
        await incidents.SaveAsync(incident, cancellationToken);
        AuditContext.SetResourceId(http, incident.Id);
        return Results.Json(incident, StatusPagesJsonContext.Default.StatusIncident, statusCode: StatusCodes.Status201Created);
    }

    /// <summary>A 400 naming the first id that is not a component of <paramref name="page"/>; null when every id is.</summary>
    private static IResult? UnknownComponent(IReadOnlyList<Guid>? requested, StatusPage page) =>
        StatusIncidents.FirstUnknownComponent(requested, page.Components) is { } id
            ? Results.Problem($"Component {id} is not on this status page.", statusCode: StatusCodes.Status400BadRequest)
            : null;

    private static async Task<IResult> HandleUpdateIncidentAsync(Guid id, Guid incidentId, HttpContext http, IStatusPageQueryService pages, IStatusIncidentQueryService incidents, TimeProvider time, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadBodyAsync(http, StatusPagesJsonContext.Default.StatusIncidentUpdateRequest, r => r.Validate(), cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var existing = await incidents.GetAsync(id, incidentId, cancellationToken);
        if (existing is null)
        {
            return Results.NotFound();
        }

        if (request!.Components is not null && await pages.GetAsync(id, cancellationToken) is { } page && UnknownComponent(request.Components, page) is { } unknown)
        {
            return unknown;
        }

        var (updated, error) = StatusIncidents.AddUpdate(existing, request!, time.GetUtcNow());
        if (updated is null)
        {
            return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        await incidents.SaveAsync(updated, cancellationToken);
        return Results.Json(updated, StatusPagesJsonContext.Default.StatusIncident);
    }

    private static async Task<IResult> HandleDeleteIncidentAsync(Guid id, Guid incidentId, IStatusIncidentQueryService incidents, CancellationToken cancellationToken) =>
        await incidents.DeleteAsync(id, incidentId, cancellationToken) ? Results.NoContent() : Results.NotFound();

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

    private static async Task<(T? Body, IResult? Problem)> ReadBodyAsync<T>(
        HttpContext http,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo,
        Func<T, string?> validate,
        CancellationToken cancellationToken)
        where T : class
    {
        T? body;
        try
        {
            body = await ApiSerialization.ReadAsync(http, typeInfo, cancellationToken);
        }
        catch (JsonException ex)
        {
            return (null, Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }

        if (body is null)
        {
            return (null, Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest));
        }

        return validate(body) is { } error ? (null, Results.Problem(error, statusCode: StatusCodes.Status400BadRequest)) : (body, null);
    }
}
