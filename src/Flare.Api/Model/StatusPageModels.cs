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

    /// <summary>A host the page is also served on (ADR-0165), e.g. <c>status.acme.com</c>; empty for none. Unique across pages.</summary>
    public string Domain { get; init; } = "";

    /// <summary>An https image URL shown above the title; empty for none.</summary>
    public string LogoUrl { get; init; } = "";

    /// <summary><c>#rrggbb</c> used for the page's accent; empty for the default.</summary>
    public string AccentColor { get; init; } = "";

    /// <summary>An https or mailto link shown as "Contact support"; empty for none.</summary>
    public string SupportUrl { get; init; } = "";

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Response body for <c>GET /api/public/status/domain/{host}</c>.</summary>
public sealed record StatusDomainResponse(string Slug);

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

    /// <summary>Null leaves an existing page's value as it is; an empty string clears it (ADR-0165).</summary>
    public string? Domain { get; init; }

    public string? LogoUrl { get; init; }

    public string? AccentColor { get; init; }

    public string? SupportUrl { get; init; }

    public const int MaxUrlLength = 2_048;

    /// <summary>A lower-cased DNS host name with at least two labels: no scheme, port, path or IP address.</summary>
    public static bool IsValidDomain(string domain)
    {
        if (domain.Length is 0 or > 253 || !domain.Contains('.') || System.Net.IPAddress.TryParse(domain, out _))
        {
            return false;
        }

        return domain.Split('.').All(label =>
            label.Length is > 0 and <= 63
            && label[0] != '-' && label[^1] != '-'
            && label.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-'));
    }

    public static bool IsValidAccentColor(string color) =>
        color.Length == 7 && color[0] == '#' && color[1..].All(Uri.IsHexDigit);

    /// <summary>An absolute https URL; when <paramref name="allowMailto"/>, also a <c>mailto:</c> address.</summary>
    public static bool IsValidLink(string url, bool allowMailto)
    {
        if (url.Length > MaxUrlLength || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps && uri.UserInfo.Length == 0 || allowMailto && uri.Scheme == Uri.UriSchemeMailto && uri.UserInfo.Length > 0 && uri.Host.Length > 0;
    }

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

        if (Domain is { Length: > 0 } domain && !IsValidDomain(domain.Trim().ToLowerInvariant()))
        {
            return "domain must be a host name such as status.example.com, with no scheme, port or path.";
        }

        if (AccentColor is { Length: > 0 } color && !IsValidAccentColor(color.Trim()))
        {
            return "accentColor must look like #1a73e8.";
        }

        if (LogoUrl is { Length: > 0 } logo && !IsValidLink(logo.Trim(), allowMailto: false))
        {
            return "logoUrl must be an https URL.";
        }

        if (SupportUrl is { Length: > 0 } support && !IsValidLink(support.Trim(), allowMailto: true))
        {
            return "supportUrl must be an https URL or a mailto: link.";
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

/// <summary>One component as the public page shows it. <see cref="Key"/> is an opaque per-page handle (a hash of page and component) a visitor can subscribe to; it reveals nothing about the monitor or SLO behind it.</summary>
public sealed record PublicStatusComponent(string Name, StatusState State, double? UptimePercent, IReadOnlyList<StatusDay> Days, Guid Key = default);

/// <summary>Response body for <c>GET /api/public/status/{slug}</c>. Carries nothing internal: no ids, targets or monitor names. <see cref="Subscribable"/> says whether visitors can sign up for incident emails (SMTP and a public URL are configured). <see cref="Slug"/> and the branding fields come from the page settings (ADR-0165). <see cref="Incidents"/> are the open ones, then those resolved within <see cref="Status.StatusIncidents.RecentDays"/> days.</summary>
public sealed record PublicStatusPage(string Title, string Description, StatusState Overall, DateTimeOffset GeneratedAt, IReadOnlyList<PublicStatusComponent> Components, IReadOnlyList<PublicStatusIncident> Incidents, bool Subscribable = false, string Slug = "", string LogoUrl = "", string AccentColor = "", string SupportUrl = "");

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

    /// <summary>The <see cref="PublicStatusComponent.Key"/>s this subscriber wants incidents for (ADR-0163). Empty means every component.</summary>
    public IReadOnlyList<Guid> Components { get; init; } = [];

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

public sealed record StatusSubscriberListResponse(IReadOnlyList<StatusSubscriber> Subscribers);

/// <summary>Body of <c>POST /api/public/status/{slug}/subscribe</c>.</summary>
public sealed record StatusSubscribeRequest
{
    public string? Email { get; init; }

    /// <summary>Component keys from the public page; null or empty subscribes to every component.</summary>
    public IReadOnlyList<Guid>? Components { get; init; }
}

/// <summary>Body of the confirm and unsubscribe posts: the signed token from the email link.</summary>
public sealed record StatusSubscriptionTokenRequest
{
    public string? Token { get; init; }
}

/// <summary>What a confirm or unsubscribe link is for, so the page can say which status page it concerns.</summary>
public sealed record StatusSubscriptionInfo(string PageTitle, string Email);

/// <summary>One component a subscriber can choose; <see cref="Key"/> is the same opaque handle the public page shows.</summary>
public sealed record StatusSubscriptionComponent(Guid Key, string Name);

/// <summary>What a preferences link shows: the page's components and the keys currently chosen (empty = every component).</summary>
public sealed record StatusSubscriptionPreferences(string PageTitle, string Email, IReadOnlyList<StatusSubscriptionComponent> Components, IReadOnlyList<Guid> Selected);

/// <summary>Body of <c>POST /api/public/status/subscriptions/preferences</c>: the signed token and the component keys to hear about (null or empty = every component).</summary>
public sealed record StatusSubscriptionPreferencesRequest
{
    public string? Token { get; init; }

    public IReadOnlyList<Guid>? Components { get; init; }
}
