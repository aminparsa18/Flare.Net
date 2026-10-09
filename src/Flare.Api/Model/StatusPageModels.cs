namespace Flare.Api.Model;

/// <summary>What a <see cref="StatusPageComponent"/> reads its health from.</summary>
public enum StatusComponentKind
{
    /// <summary>A synthetic monitor (ADR-0128), by id.</summary>
    Monitor,

    /// <summary>An SLO (ADR-0108), by id.</summary>
    Slo,
}

/// <summary>A component's (or a whole page's) health as a visitor sees it.</summary>
public enum StatusState
{
    /// <summary>No usable data: never probed, stale, or an SLO with no traffic.</summary>
    Unknown,

    Operational,

    /// <summary>Some probe locations are down, or an SLO has spent more than its error budget.</summary>
    Degraded,

    /// <summary>Every probe location is down.</summary>
    Outage,
}

/// <summary>
/// One row of a status page: a public display name over a monitor or SLO. The display name is
/// what visitors see, so an internal monitor name, target or SLO name is never exposed.
/// </summary>
public sealed record StatusPageComponent(string Name, StatusComponentKind Kind, Guid RefId);

/// <summary>
/// A public, read-only page of service health, served without a session at
/// <c>GET /api/public/status/{slug}</c> while <see cref="Enabled"/>. JSON only - not MemoryPack'd.
/// See <c>docs-internal/adr/0158-status-pages.md</c>.
/// </summary>
public sealed record StatusPage
{
    public required Guid Id { get; init; }

    /// <summary>The page's URL segment: <c>/status/{slug}</c>. Unique across pages.</summary>
    public required string Slug { get; init; }

    public required string Title { get; init; }

    public string Description { get; init; } = "";

    /// <summary>Off by default: a page is published only when someone turns it on.</summary>
    public bool Enabled { get; init; }

    public IReadOnlyList<StatusPageComponent> Components { get; init; } = [];

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record StatusPageListResponse(IReadOnlyList<StatusPage> Pages);

/// <summary>Create/update body for <c>/api/status-pages</c>.</summary>
public sealed record StatusPageRequest
{
    public const int MaxTitleLength = 120;
    public const int MaxDescriptionLength = 1_000;
    public const int MaxComponents = 50;
    public const int MaxComponentNameLength = 100;

    public required string Slug { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public bool? Enabled { get; init; }

    public IReadOnlyList<StatusPageComponent>? Components { get; init; }

    /// <summary>Lowercase letters, digits and inner hyphens, 1-64 characters: safe in a URL path with no escaping.</summary>
    public static bool IsValidSlug(string slug) =>
        slug.Length is > 0 and <= 64
        && slug[0] != '-' && slug[^1] != '-'
        && slug.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    /// <summary>Returns an error message, or null when this request is valid.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Slug) || !IsValidSlug(Slug.Trim()))
        {
            return "slug must be 1-64 lowercase letters, digits or hyphens, and not start or end with a hyphen.";
        }

        if (string.IsNullOrWhiteSpace(Title) || Title.Trim().Length > MaxTitleLength)
        {
            return $"title is required and must be at most {MaxTitleLength} characters.";
        }

        if ((Description?.Length ?? 0) > MaxDescriptionLength)
        {
            return $"description must be at most {MaxDescriptionLength} characters.";
        }

        var components = Components ?? [];
        if (components.Count > MaxComponents)
        {
            return $"a page can have at most {MaxComponents} components.";
        }

        foreach (var component in components)
        {
            if (string.IsNullOrWhiteSpace(component.Name) || component.Name.Trim().Length > MaxComponentNameLength)
            {
                return $"every component needs a name of at most {MaxComponentNameLength} characters.";
            }

            if (component.RefId == Guid.Empty)
            {
                return "every component needs a monitor or SLO id.";
            }
        }

        return null;
    }
}

/// <summary>One day of a component's history; <see cref="UptimePercent"/> is null when nothing was recorded that day.</summary>
public sealed record StatusDay(string Date, double? UptimePercent);

/// <summary>One component as the public page shows it.</summary>
public sealed record PublicStatusComponent(string Name, StatusState State, double? UptimePercent, IReadOnlyList<StatusDay> Days);

/// <summary>Response body for <c>GET /api/public/status/{slug}</c>. Carries nothing internal: no ids, targets or monitor names. <see cref="Incidents"/> are the open ones, then those resolved within <see cref="Status.StatusIncidents.RecentDays"/> days.</summary>
public sealed record PublicStatusPage(string Title, string Description, StatusState Overall, DateTimeOffset GeneratedAt, IReadOnlyList<PublicStatusComponent> Components, IReadOnlyList<PublicStatusIncident> Incidents);

/// <summary>Where an incident is in its life; each written update carries one, and the latest is the incident's.</summary>
public enum StatusIncidentStatus
{
    Investigating,
    Identified,
    Monitoring,
    Resolved,
}

/// <summary>One entry in an incident's timeline.</summary>
public sealed record StatusIncidentUpdate(DateTimeOffset At, StatusIncidentStatus Status, string Message);

/// <summary>
/// A written incident on a <see cref="StatusPage"/>: a title and a timeline of updates (oldest first).
/// Its status is the latest update's, and it is resolved once that update is <see cref="StatusIncidentStatus.Resolved"/>.
/// </summary>
public sealed record StatusIncident
{
    public required Guid Id { get; init; }

    public required Guid PageId { get; init; }

    public required string Title { get; init; }

    public IReadOnlyList<StatusIncidentUpdate> Updates { get; init; } = [];

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public StatusIncidentStatus Status => Updates.Count == 0 ? StatusIncidentStatus.Investigating : Updates[^1].Status;

    public DateTimeOffset? ResolvedAt => Status == StatusIncidentStatus.Resolved ? Updates[^1].At : null;
}

public sealed record StatusIncidentListResponse(IReadOnlyList<StatusIncident> Incidents);

/// <summary>Body for <c>POST /api/status-pages/{id}/incidents</c> (opens an incident with its first update).</summary>
public sealed record StatusIncidentRequest
{
    public const int MaxTitleLength = 200;
    public const int MaxMessageLength = 4_000;

    public required string Title { get; init; }

    /// <summary>Defaults to <see cref="StatusIncidentStatus.Investigating"/>.</summary>
    public StatusIncidentStatus? Status { get; init; }

    public required string Message { get; init; }

    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Title) || Title.Trim().Length > MaxTitleLength)
        {
            return $"title is required and must be at most {MaxTitleLength} characters.";
        }

        return StatusIncidentUpdateRequest.ValidateMessage(Message);
    }
}

/// <summary>Body for <c>POST /api/status-pages/{id}/incidents/{incidentId}/updates</c>.</summary>
public sealed record StatusIncidentUpdateRequest
{
    public required StatusIncidentStatus Status { get; init; }

    public required string Message { get; init; }

    public string? Validate() => ValidateMessage(Message);

    internal static string? ValidateMessage(string? message) =>
        string.IsNullOrWhiteSpace(message) || message.Trim().Length > StatusIncidentRequest.MaxMessageLength
            ? $"message is required and must be at most {StatusIncidentRequest.MaxMessageLength} characters."
            : null;
}

/// <summary>An incident as the public page shows it: timeline newest first, no ids.</summary>
public sealed record PublicStatusIncident(string Title, StatusIncidentStatus Status, DateTimeOffset StartedAt, DateTimeOffset? ResolvedAt, IReadOnlyList<StatusIncidentUpdate> Updates);
