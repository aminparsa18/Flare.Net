using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>The saved-views API: named, reloadable, shareable per-page filter/selection state, under <c>/api/views</c>.</summary>
/// <remarks>
/// Mirrors <see cref="AlertEndpoints"/>'s shape: POST/PUT + JSON body for anything with a
/// structured payload, manual <see cref="JsonSerializer.DeserializeAsync"/> against the
/// source-gen <see cref="SavedViewsJsonContext"/>, <see cref="Results.Problem"/> on 400s.
/// </remarks>
public static class SavedViewEndpoints
{
    public static IEndpointRouteBuilder MapSavedViewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/views", HandleCreateAsync);
        endpoints.MapGet("/api/views", HandleListAsync);
        endpoints.MapGet("/api/views/{id:guid}", HandleGetAsync);
        endpoints.MapPut("/api/views/{id:guid}", HandleUpdateAsync);
        endpoints.MapDelete("/api/views/{id:guid}", HandleDeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, ISavedViewQueryService views, CancellationToken cancellationToken)
    {
        SavedViewRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(http.Request.Body, SavedViewsJsonContext.Default.SavedViewRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var view = await views.CreateAsync(request, cancellationToken);
        return Results.Json(view, SavedViewsJsonContext.Default.SavedView, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAsync(SavedViewPageType? pageType, ISavedViewQueryService views, CancellationToken cancellationToken)
    {
        var list = await views.ListAsync(pageType, cancellationToken);
        return Results.Json(new SavedViewListResponse { Views = list }, SavedViewsJsonContext.Default.SavedViewListResponse);
    }

    private static async Task<IResult> HandleGetAsync(Guid id, ISavedViewQueryService views, CancellationToken cancellationToken)
    {
        var view = await views.GetAsync(id, cancellationToken);
        return view is null ? Results.NotFound() : Results.Json(view, SavedViewsJsonContext.Default.SavedView);
    }

    private static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, ISavedViewQueryService views, CancellationToken cancellationToken)
    {
        SavedViewRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(http.Request.Body, SavedViewsJsonContext.Default.SavedViewRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var view = await views.UpdateAsync(id, request, cancellationToken);
        return view is null ? Results.NotFound() : Results.Json(view, SavedViewsJsonContext.Default.SavedView);
    }

    private static async Task<IResult> HandleDeleteAsync(Guid id, ISavedViewQueryService views, CancellationToken cancellationToken)
    {
        var deleted = await views.DeleteAsync(id, cancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }
}
