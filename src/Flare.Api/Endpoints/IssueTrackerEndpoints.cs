using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.IssueTrackers;

namespace Flare.Api.Endpoints;

/// <summary>
/// Per-service issue tracker config behind the /errors "New Jira issue" link - see
/// docs-internal/adr/0274-jira-new-issue-link.md. <see cref="MapIssueTrackerReadEndpoints"/> (any
/// signed-in user - the dashboard builds the link) and <see cref="MapIssueTrackerWriteEndpoints"/>
/// (Admin-only: it changes where every user's links point) are mapped onto different route groups
/// in <c>Program.cs</c>, like <see cref="SourceLinkEndpoints"/>.
/// </summary>
public static class IssueTrackerEndpoints
{
    public static IEndpointRouteBuilder MapIssueTrackerReadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/issue-trackers", HandleListAsync);
        return endpoints;
    }

    public static IEndpointRouteBuilder MapIssueTrackerWriteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/issue-trackers/{serviceName}", HandleSetAsync);
        endpoints.MapDelete("/api/issue-trackers/{serviceName}", HandleDeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleListAsync(HttpContext http, IIssueTrackerStore store, CancellationToken cancellationToken)
    {
        var all = await store.GetAllAsync(cancellationToken);
        var response = new IssueTrackerListResponse
        {
            Trackers = [.. all.Select(c => new IssueTrackerDto
            {
                ServiceName = c.ServiceName,
                Provider = c.Provider,
                SiteUrl = c.SiteUrl,
                ProjectId = c.ProjectId,
                IssueTypeId = c.IssueTypeId
            })]
        };
        return ApiSerialization.Write(http, response, IssueTrackersJsonContext.Default.IssueTrackerListResponse);
    }

    private static async Task<IResult> HandleSetAsync(HttpContext http, string serviceName, IIssueTrackerStore store, CancellationToken cancellationToken)
    {
        IssueTrackerDto? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, IssueTrackersJsonContext.Default.IssueTrackerDto, cancellationToken);
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
            new IssueTrackerConfig(serviceName, request.Provider, request.SiteUrl.Trim().TrimEnd('/'), request.ProjectId, request.IssueTypeId),
            cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> HandleDeleteAsync(string serviceName, IIssueTrackerStore store, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(serviceName, cancellationToken);
        return Results.NoContent();
    }
}
