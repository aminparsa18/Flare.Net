using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.SourceLinks;

namespace Flare.Api.Endpoints;

/// <summary>
/// Per-service source repository config behind the /errors stack-trace "open in repo" links
/// - see docs-internal/adr/0095-exception-source-links.md. <see cref="MapSourceLinkReadEndpoints"/>
/// (any signed-in user - the dashboard needs it to render links) and
/// <see cref="MapSourceLinkWriteEndpoints"/> (Admin-only: it changes where every user's
/// links point) are mapped onto different route groups in <c>Program.cs</c>.
/// </summary>
public static class SourceLinkEndpoints
{
    public static IEndpointRouteBuilder MapSourceLinkReadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/source-links", HandleListAsync);
        return endpoints;
    }

    public static IEndpointRouteBuilder MapSourceLinkWriteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/source-links/{serviceName}", HandleSetAsync);
        endpoints.MapDelete("/api/source-links/{serviceName}", HandleDeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleListAsync(HttpContext http, ISourceLinkStore store, CancellationToken cancellationToken)
    {
        var all = await store.GetAllAsync(cancellationToken);
        var response = new SourceLinkListResponse
        {
            Links = [.. all.Select(c => new SourceLinkDto
            {
                ServiceName = c.ServiceName,
                Provider = c.Provider,
                RepoUrl = c.RepoUrl,
                DefaultRef = c.DefaultRef,
                PathPrefix = c.PathPrefix
            })]
        };
        return ApiSerialization.Write(http, response, SourceLinksJsonContext.Default.SourceLinkListResponse);
    }

    private static async Task<IResult> HandleSetAsync(HttpContext http, string serviceName, ISourceLinkStore store, CancellationToken cancellationToken)
    {
        SourceLinkDto? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, SourceLinksJsonContext.Default.SourceLinkDto, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Validate() is { } error)
        {
            return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        // The route's service name is authoritative; any serviceName in the body is ignored.
        await store.SetAsync(
            new SourceLinkConfig(serviceName, request.Provider, request.RepoUrl.Trim().TrimEnd('/'), request.DefaultRef.Trim(), request.PathPrefix.Trim()),
            cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> HandleDeleteAsync(string serviceName, ISourceLinkStore store, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(serviceName, cancellationToken);
        return Results.NoContent();
    }
}
