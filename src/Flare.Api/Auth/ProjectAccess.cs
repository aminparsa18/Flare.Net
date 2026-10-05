using Flare.Identity.Projects;
using Flare.Identity.Users;

namespace Flare.Api.Auth;

/// <summary>
/// What the current caller may do with project-owned config objects (dashboards, saved views,
/// alert rules, SLOs, ingest keys; ADR-0123 phase 3). Pure: built from the caller's memberships
/// by <see cref="ProjectScopeMiddleware"/> and read by the endpoints.
/// </summary>
/// <remarks>
/// A null <c>projectId</c> is an instance-wide object: readable by everyone, and writable under
/// the route's own global-role policy (<c>RequireMember</c>), exactly as before projects existed.
/// A set <c>projectId</c> narrows that: only members read it, only members with the project role
/// <c>Admin</c> or <c>Member</c> write it. The project role can only narrow the global role,
/// never widen it, so a global Viewer who is a project Member still can't edit (the route's
/// policy rejects them first). Global Admins and auth-disabled instances are unrestricted.
/// </remarks>
public sealed class ProjectAccess
{
    public static readonly ProjectAccess Unrestricted = new(null);

    private readonly IReadOnlyDictionary<Guid, UserRole>? roles;

    private ProjectAccess(IReadOnlyDictionary<Guid, UserRole>? roles) => this.roles = roles;

    public static ProjectAccess ForMember(IEnumerable<ProjectMembership> memberships) =>
        new(memberships.GroupBy(m => m.ProjectId).ToDictionary(g => g.Key, g => g.Select(m => m.Role).Min()));

    public bool IsUnrestricted => roles is null;

    /// <summary>Whether the object is visible: instance-wide, or the caller is a member of its project.</summary>
    public bool CanRead(Guid? projectId) =>
        roles is null || projectId is not { } id || roles.ContainsKey(id);

    /// <summary>Whether the caller may create/edit/delete an object in the project (null = instance-wide).</summary>
    public bool CanWrite(Guid? projectId) =>
        roles is null || projectId is not { } id || (roles.TryGetValue(id, out var role) && role is UserRole.Admin or UserRole.Member);

    /// <summary>Whether the caller administers the project: may override per-object ownership inside it.</summary>
    public bool CanManage(Guid? projectId) =>
        roles is null || (projectId is { } id && roles.TryGetValue(id, out var role) && role == UserRole.Admin);

    /// <summary>The ids of the projects the caller belongs to, or null when unrestricted.</summary>
    public IReadOnlyCollection<Guid>? MemberProjectIds => roles?.Keys.ToArray();

    public IReadOnlyList<T> Filter<T>(IEnumerable<T> items, Func<T, Guid?> projectId) =>
        roles is null ? [.. items] : [.. items.Where(i => CanRead(projectId(i)))];
}

public static class ProjectAccessHttpContextExtensions
{
    private const string ItemKey = "Flare.ProjectAccess";

    public static void SetProjectAccess(this HttpContext http, ProjectAccess access) => http.Items[ItemKey] = access;

    /// <summary>The caller's project access; unrestricted when the middleware didn't run (unit tests, workers).</summary>
    public static ProjectAccess GetProjectAccess(this HttpContext http) =>
        http.Items.TryGetValue(ItemKey, out var value) && value is ProjectAccess access ? access : ProjectAccess.Unrestricted;
}
