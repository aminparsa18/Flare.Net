using Flare.Api.Model;
using Flare.Identity.Projects;

namespace Flare.Api.ResourceGraph;

/// <summary>
/// Narrows the shared resource-graph snapshot to the caller's projects (ADR-0123). The topology
/// pollers build one snapshot in a background loop where <see cref="Query.ServiceScope.Current"/>
/// is null, so the producer overlay (service names, last-seen times, ingest edges) covers every
/// project and has to be filtered per reader. Container/pod/Service nodes describe Flare's own
/// stack (<c>flare.role</c> pods only), not other projects' workloads, and stay visible.
/// </summary>
public static class ResourceGraphScopeFilter
{
    /// <summary>Returns <paramref name="snapshot"/> unchanged when <paramref name="allowedServices"/> is null (unrestricted).</summary>
    public static ResourceGraphSnapshot Apply(ResourceGraphSnapshot snapshot, IReadOnlyList<string>? allowedServices)
    {
        if (allowedServices is null)
        {
            return snapshot;
        }

        var hidden = snapshot.Producers
            .Where(p => !ProjectServicePattern.MatchesAny(allowedServices, p.ServiceName))
            .Select(p => p.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (hidden.Count == 0)
        {
            return snapshot;
        }

        return snapshot with
        {
            Producers = [.. snapshot.Producers.Where(p => !hidden.Contains(p.Id))],
            Edges = [.. snapshot.Edges.Where(e => !hidden.Contains(e.SourceRole) && !hidden.Contains(e.TargetRole))],
        };
    }
}
