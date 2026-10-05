using System.Text.Json;
using Flare.Api.Auth;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.Projects;

namespace Flare.Api.Endpoints;

/// <summary>
/// Project CRUD and membership under <c>/api/projects</c> (ADR-0123) - Admin-only, mapped on
/// the admin route group in <c>Program.cs</c>. Phase 1 only manages the data; enforcing a
/// project's service patterns on queries is a later phase.
/// </summary>
public static class ProjectEndpoints
{
    /// <summary>
    /// <c>GET /api/projects/mine</c>: the projects the caller belongs to (all of them for a global
    /// Admin or with auth off), with their own role. Mapped on the authenticated group so the
    /// dashboard's project switcher and form pickers work for non-admins.
    /// </summary>
    public static IEndpointRouteBuilder MapMyProjectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/projects/mine", HandleListMineAsync);
        return endpoints;
    }

    public static IEndpointRouteBuilder MapProjectEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/projects", HandleListAsync);
        endpoints.MapPost("/api/projects", HandleCreateAsync);
        endpoints.MapPut("/api/projects/{id:guid}", HandleUpdateAsync);
        endpoints.MapDelete("/api/projects/{id:guid}", HandleDeleteAsync);
        endpoints.MapGet("/api/projects/{id:guid}/members", HandleListMembersAsync);
        endpoints.MapPut("/api/projects/{id:guid}/members/{userId:guid}", HandleSetMemberAsync);
        endpoints.MapDelete("/api/projects/{id:guid}/members/{userId:guid}", HandleRemoveMemberAsync);
        return endpoints;
    }

    internal static async Task<IResult> HandleListAsync(HttpContext http, IProjectStore store, CancellationToken cancellationToken)
    {
        var projects = await store.ListAsync(cancellationToken);
        return ApiSerialization.Write(http, new ProjectListResponse { Projects = [.. projects.Select(p => p.ToDto())] }, ProjectsJsonContext.Default.ProjectListResponse);
    }

    internal static async Task<IResult> HandleListMineAsync(HttpContext http, IProjectStore store, CancellationToken cancellationToken)
    {
        var access = http.GetProjectAccess();
        var projects = await store.ListAsync(cancellationToken);
        var mine = projects
            .Select(p => (Project: p, Role: access.RoleIn(p.Id)))
            .Where(x => x.Role is not null)
            .OrderBy(x => x.Project.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new MyProjectDto { Id = x.Project.Id, Name = x.Project.Name, Description = x.Project.Description, Role = x.Role!.Value });
        return ApiSerialization.Write(http, new MyProjectListResponse { Projects = [.. mine] }, ProjectsJsonContext.Default.MyProjectListResponse);
    }

    internal static async Task<IResult> HandleCreateAsync(HttpContext http, IProjectStore store, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadProjectAsync(http, cancellationToken);
        if (request is null)
        {
            return problem!;
        }

        try
        {
            var created = await store.CreateAsync(request.Name.Trim(), request.Description ?? "", request.ServicePatterns ?? [], cancellationToken);
            return ApiSerialization.Write(http, created.ToDto(), ProjectsJsonContext.Default.ProjectDto, statusCode: StatusCodes.Status201Created);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    internal static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, IProjectStore store, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadProjectAsync(http, cancellationToken);
        if (request is null)
        {
            return problem!;
        }

        try
        {
            var updated = await store.UpdateAsync(id, request.Name.Trim(), request.Description ?? "", request.ServicePatterns ?? [], cancellationToken);
            return updated is null
                ? Results.NotFound()
                : ApiSerialization.Write(http, updated.ToDto(), ProjectsJsonContext.Default.ProjectDto);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    internal static async Task<IResult> HandleDeleteAsync(Guid id, IProjectStore store, CancellationToken cancellationToken) =>
        await store.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();

    internal static async Task<IResult> HandleListMembersAsync(Guid id, HttpContext http, IProjectStore store, CancellationToken cancellationToken)
    {
        if (await store.GetAsync(id, cancellationToken) is null)
        {
            return Results.NotFound();
        }

        var members = await store.ListMembersAsync(id, cancellationToken);
        return ApiSerialization.Write(
            http,
            new ProjectMemberListResponse { Members = [.. members.Select(m => new ProjectMemberDto { UserId = m.UserId, Username = m.Username, Role = m.Role })] },
            ProjectsJsonContext.Default.ProjectMemberListResponse);
    }

    internal static async Task<IResult> HandleSetMemberAsync(Guid id, Guid userId, HttpContext http, IProjectStore store, CancellationToken cancellationToken)
    {
        SetProjectMemberRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, ProjectsJsonContext.Default.SetProjectMemberRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        return await store.SetMemberAsync(id, userId, request.Role, cancellationToken) ? Results.NoContent() : Results.NotFound();
    }

    internal static async Task<IResult> HandleRemoveMemberAsync(Guid id, Guid userId, IProjectStore store, CancellationToken cancellationToken) =>
        await store.RemoveMemberAsync(id, userId, cancellationToken) ? Results.NoContent() : Results.NotFound();

    private static async Task<(ProjectDto? Request, IResult? Problem)> ReadProjectAsync(HttpContext http, CancellationToken cancellationToken)
    {
        ProjectDto? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, ProjectsJsonContext.Default.ProjectDto, cancellationToken);
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
