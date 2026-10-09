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

    /// <summary>Saved notification channels (ADR-0161) that hear about every incident opened or updated on this page.</summary>
    public IReadOnlyList<Guid> SubscriberChannelIds { get; init; } = [];

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
    public const int MaxSubscribers = 20;

    public required string Slug { get; init; }

    public required string Title { get; init; }

    public string? Description { get; init; }

    public bool? Enabled { get; init; }

    public IReadOnlyList<StatusPageComponent>? Components { get; init; }

    /// <summary>Null leaves an existing page's subscribers as they are; an empty list clears them.</summary>
    public IReadOnlyList<Guid>? SubscriberChannelIds { get; init; }

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

        if ((SubscriberChannelIds?.Count ?? 0) > MaxSubscribers)
        {
            return $"a page can have at most {MaxSubscribers} subscribed channels.";
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

/// <summary>Response body for <c>GET /api/public/status/{slug}</c>. Carries nothing internal: no ids, targets or monitor names. <see cref="Subscribable"/> says whether visitors can sign up for incident emails (SMTP and a public URL are configured). <see cref="Incidents"/> are the open ones, then those resolved within <see cref="Status.StatusIncidents.RecentDays"/> days.</summary>
public sealed record PublicStatusPage(string Title, string Description, StatusState Overall, DateTimeOffset GeneratedAt, IReadOnlyList<PublicStatusComponent> Components, IReadOnlyList<PublicStatusIncident> Incidents, bool Subscribable = false);

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

    /// <summary>The <see cref="StatusPageComponent.RefId"/>s of the page components this incident affects; empty when it is not tied to one.</summary>
    public IReadOnlyList<Guid> Components { get; init; } = [];

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

    /// <summary>The page components affected, by <see cref="StatusPageComponent.RefId"/>. Each must be on the page.</summary>
    public IReadOnlyList<Guid>? Components { get; init; }

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

    /// <summary>Replaces the affected components when set; null leaves them as they are, an empty list clears them.</summary>
    public IReadOnlyList<Guid>? Components { get; init; }

    public string? Validate() => ValidateMessage(Message);

    internal static string? ValidateMessage(string? message) =>
        string.IsNullOrWhiteSpace(message) || message.Trim().Length > StatusIncidentRequest.MaxMessageLength
            ? $"message is required and must be at most {StatusIncidentRequest.MaxMessageLength} characters."
            : null;
}

/// <summary>An incident as the public page shows it: timeline newest first, no ids. <see cref="Components"/> are the display names of the page components it affects.</summary>
public sealed record PublicStatusIncident(string Title, StatusIncidentStatus Status, DateTimeOffset StartedAt, DateTimeOffset? ResolvedAt, IReadOnlyList<StatusIncidentUpdate> Updates, IReadOnlyList<string> Components);

/// <summary>An email address that asked to hear about a status page's incidents (ADR-0162). Only <see cref="Verified"/> addresses are mailed.</summary>
public sealed record StatusSubscriber
{
    public required Guid Id { get; init; }

    public required Guid PageId { get; init; }

    /// <summary>Lower-cased.</summary>
    public required string Email { get; init; }

    public bool Verified { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record StatusSubscriberListResponse(IReadOnlyList<StatusSubscriber> Subscribers);

/// <summary>Body of <c>POST /api/public/status/{slug}/subscribe</c>.</summary>
public sealed record StatusSubscribeRequest
{
    public string? Email { get; init; }
}

/// <summary>Body of the confirm and unsubscribe posts: the signed token from the email link.</summary>
public sealed record StatusSubscriptionTokenRequest
{
    public string? Token { get; init; }
}

/// <summary>What a confirm or unsubscribe link is for, so the page can say which status page it concerns.</summary>
public sealed record StatusSubscriptionInfo(string PageTitle, string Email);
