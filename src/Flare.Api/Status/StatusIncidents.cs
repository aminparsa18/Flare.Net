using Flare.Api.Model;

namespace Flare.Api.Status;

/// <summary>
/// The pure rules for status page incidents: opening one, appending an update, and choosing which a public
/// page shows. Free of ClickHouse so they can be unit-tested. See <c>docs-internal/adr/0159-status-page-incidents.md</c>.
/// </summary>
public static class StatusIncidents
{
    /// <summary>How long a resolved incident stays on the public page.</summary>
    public const int RecentDays = 14;

    /// <summary>How many updates one incident can carry.</summary>
    public const int MaxUpdates = 100;

    public static StatusIncident Open(Guid pageId, StatusIncidentRequest request, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        PageId = pageId,
        Title = request.Title.Trim(),
        Updates = [new StatusIncidentUpdate(now, request.Status ?? StatusIncidentStatus.Investigating, request.Message.Trim())],
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>Appends an update; returns an error message instead when the incident cannot take one.</summary>
    public static (StatusIncident? Incident, string? Error) AddUpdate(StatusIncident incident, StatusIncidentUpdateRequest request, DateTimeOffset now)
    {
        if (incident.Updates.Count >= MaxUpdates)
        {
            return (null, $"an incident can have at most {MaxUpdates} updates.");
        }

        var update = new StatusIncidentUpdate(now, request.Status, request.Message.Trim());
        return (incident with { Updates = [.. incident.Updates, update], UpdatedAt = now }, null);
    }

    /// <summary>Open incidents (oldest first), then incidents resolved within <see cref="RecentDays"/> (newest first).</summary>
    public static IReadOnlyList<PublicStatusIncident> ForPublic(IEnumerable<StatusIncident> incidents, DateTimeOffset now)
    {
        var cutoff = now - TimeSpan.FromDays(RecentDays);
        var all = incidents.ToList();
        var open = all.Where(i => i.ResolvedAt is null).OrderBy(i => i.CreatedAt);
        var resolved = all.Where(i => i.ResolvedAt is { } at && at >= cutoff).OrderByDescending(i => i.ResolvedAt);
        return open.Concat(resolved).Select(i => new PublicStatusIncident(
            i.Title, i.Status, i.CreatedAt, i.ResolvedAt, i.Updates.Reverse().ToList())).ToList();
    }
}
