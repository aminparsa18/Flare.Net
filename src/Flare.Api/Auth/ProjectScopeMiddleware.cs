using System.Security.Claims;
using Flare.Api.Query;
using Flare.Identity.Projects;
using Flare.Identity.Users;

namespace Flare.Api.Auth;

/// <summary>
/// Sets <see cref="ServiceScope.Current"/> for the request from the caller's project
/// memberships (ADR-0123). Runs after authentication; unauthenticated requests (auth disabled)
/// and global Admins stay unrestricted.
/// </summary>
public sealed class ProjectScopeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IProjectStore projects)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true || user.IsInRole(nameof(UserRole.Admin)))
        {
            await next(context);
            return;
        }

        var all = await projects.ListAsync(context.RequestAborted);
        var memberships = Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? await projects.ListMembershipsAsync(id, context.RequestAborted)
            : [];
        ServiceScope.Current = ProjectScopeEvaluator.Resolve(false, all, memberships);
        try
        {
            await next(context);
        }
        finally
        {
            ServiceScope.Current = null;
        }
    }
}
