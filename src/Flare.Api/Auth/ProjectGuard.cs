using Flare.Identity.Projects;

namespace Flare.Api.Auth;

/// <summary>
/// Endpoint-side checks for project-owned config objects (ADR-0123 phase 3), shared by
/// dashboards, saved views, alert rules, SLOs and ingest keys so they all answer the same way:
/// 404 for an object the caller can't see (so existence doesn't leak), 403 for one they can see
/// but not change, 400 for a project that doesn't exist.
/// </summary>
public static class ProjectGuard
{
    /// <summary>Request value to stored value on create: null stays null, <see cref="Guid.Empty"/> also means none.</summary>
    public static Guid? Normalize(Guid? requested) => requested == Guid.Empty ? null : requested;

    /// <summary>
    /// Request value to stored value on update: omitted (null) keeps the existing project, so a
    /// client that predates projects can't un-scope an object by saving it; <see cref="Guid.Empty"/>
    /// explicitly clears it.
    /// </summary>
    public static Guid? ResolveForUpdate(Guid? existing, Guid? requested) =>
        requested is null ? existing : Normalize(requested);

    /// <summary>The caller must be able to see and change an existing object.</summary>
    public static IResult? CheckExisting(HttpContext http, Guid? projectId)
    {
        var access = http.GetProjectAccess();
        if (!access.CanRead(projectId))
        {
            return Results.NotFound();
        }

        return access.CanWrite(projectId) ? null : Results.Forbid();
    }

    /// <summary>
    /// The project an object is being created in or moved to must exist and be writable by the
    /// caller. A no-op when it equals <paramref name="current"/> (already covered by
    /// <see cref="CheckExisting"/>) or is instance-wide.
    /// </summary>
    public static async Task<IResult?> CheckTargetAsync(HttpContext http, IProjectStore projects, Guid? current, Guid? target, CancellationToken cancellationToken)
    {
        if (target is not { } id || target == current)
        {
            return null;
        }

        if (await projects.GetAsync(id, cancellationToken) is null)
        {
            return Results.Problem($"Project {id} does not exist.", statusCode: StatusCodes.Status400BadRequest);
        }

        return http.GetProjectAccess().CanWrite(id) ? null : Results.Forbid();
    }
}
