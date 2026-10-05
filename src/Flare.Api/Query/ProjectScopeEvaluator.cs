using Flare.Identity.Projects;
using Flare.Identity.Users;

namespace Flare.Api.Query;

/// <summary>Pure decision of which services a caller may see (ADR-0123). Null = unrestricted.</summary>
public static class ProjectScopeEvaluator
{
    public static IReadOnlyList<string>? Resolve(
        bool isGlobalAdmin, IReadOnlyList<Project> projects, IReadOnlyList<ProjectMembership> memberships)
    {
        // A global Admin, and an instance that has defined no projects yet, see everything:
        // creating the first project must not silently blind existing users until configured.
        if (isGlobalAdmin || projects.Count == 0)
        {
            return null;
        }

        var mine = memberships.Select(m => m.ProjectId).ToHashSet();
        return [.. projects.Where(p => mine.Contains(p.Id)).SelectMany(p => p.ServicePatterns).Distinct(StringComparer.Ordinal)];
    }
}
