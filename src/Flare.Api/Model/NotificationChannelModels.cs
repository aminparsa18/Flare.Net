using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Which channel a <see cref="NotificationChannel"/> sends through - the
/// same four <see cref="AlertRule"/>'s legacy inline fields
/// (<see cref="AlertRule.WebhookUrl"/>/<see cref="AlertRule.TelegramBotToken"/>+
/// <see cref="AlertRule.TelegramChatId"/>/<see cref="AlertRule.EmailTo"/>/
/// <see cref="AlertRule.PagerDutyRoutingKey"/>) already support - this is just those same
/// destinations factored into their own reusable, named entity. See
/// <c>docs-internal/adr/0021-reusable-notification-channels.md</c>.
/// </summary>
public enum NotificationChannelType
{
    Webhook,
    Telegram,
    Email,
    PagerDuty,
    // Appended (MemoryPack encodes the enum as its int). Both reuse WebhookUrl as their
    // destination - a Teams Workflows webhook / Discord webhook URL.
    Teams,
    Discord,
    // Appended after Discord. Uses the Jira* fields below; see ADR-0097.
    Jira,
}

/// <summary>
/// A saved, reusable notification destination - referenced by ID from zero or more
/// <see cref="AlertRule.ChannelIds"/> instead of being re-entered inline on every rule
/// that should reach it. Only the destination field(s) matching <see cref="Type"/> are
/// meaningful ("field present, meaningful only for one mode", the same convention
/// <see cref="AlertRule"/>'s own legacy channel fields already use) - the CRUD/validation
/// mirrors <see cref="AlertRule"/>'s, just for a standalone, named channel rather than a
/// rule's inline destination.
/// </summary>
[MemoryPackable]
public sealed partial record NotificationChannel
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public required NotificationChannelType Type { get; init; }

    /// <summary>See <see cref="AlertRule.WebhookUrl"/>'s doc comment. Meaningful only when <see cref="Type"/> is <see cref="NotificationChannelType.Webhook"/>.</summary>
    public string WebhookUrl { get; init; } = "";

    /// <summary>See <see cref="AlertRule.TelegramBotToken"/>'s doc comment. Meaningful only when <see cref="Type"/> is <see cref="NotificationChannelType.Telegram"/>.</summary>
    public string TelegramBotToken { get; init; } = "";

    /// <summary>See <see cref="AlertRule.TelegramChatId"/>'s doc comment. Meaningful only when <see cref="Type"/> is <see cref="NotificationChannelType.Telegram"/>.</summary>
    public string TelegramChatId { get; init; } = "";

    /// <summary>See <see cref="AlertRule.EmailTo"/>'s doc comment. Meaningful only when <see cref="Type"/> is <see cref="NotificationChannelType.Email"/>.</summary>
    public string EmailTo { get; init; } = "";

    /// <summary>See <see cref="AlertRule.PagerDutyRoutingKey"/>'s doc comment. Meaningful only when <see cref="Type"/> is <see cref="NotificationChannelType.PagerDuty"/>.</summary>
    public string PagerDutyRoutingKey { get; init; } = "";

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>
    /// Whether a rule's recovery after firing sends a "Resolved" notification through this
    /// channel (for PagerDuty, an Events API v2 <c>resolve</c> that auto-closes the incident).
    /// True by default, and for every channel created before this field existed and every
    /// legacy inline channel - an opt-out, not an opt-in. See
    /// <c>docs-internal/adr/0064-alert-resolved-notifications.md</c>. Appended after
    /// <see cref="UpdatedAt"/>, same versioning reasoning as <see cref="AlertRule.ConditionKind"/>.
    /// </summary>
    public bool SendResolved { get; init; } = true;

    /// <summary>Jira Cloud site root, e.g. <c>https://acme.atlassian.net</c>. Meaningful only when <see cref="Type"/> is <see cref="NotificationChannelType.Jira"/>. The Jira* fields are appended after <see cref="SendResolved"/> (MemoryPack versioning).</summary>
    public string JiraBaseUrl { get; init; } = "";

    /// <summary>Atlassian account email for the API token (HTTP Basic user). Jira channels only.</summary>
    public string JiraEmail { get; init; } = "";

    /// <summary>Atlassian API token (HTTP Basic password). Jira channels only.</summary>
    public string JiraApiToken { get; init; } = "";

    /// <summary>Key of the project issues are created in, e.g. <c>OPS</c>. Jira channels only.</summary>
    public string JiraProjectKey { get; init; } = "";

    /// <summary>Issue type name, e.g. <c>Bug</c>. Empty means <c>Task</c>. Jira channels only.</summary>
    public string JiraIssueType { get; init; } = "";
}

/// <summary>
/// Create/update request body for <c>/api/notification-channels</c>. Same nullable-vs-
/// non-default-initializer shape as <see cref="AlertRuleRequest"/>, and for the same
/// reason - see that type's remarks. Carries <c>[GenerateTypeScript]</c> directly (unlike
/// <see cref="AlertRuleRequest"/>): no <see cref="DateTimeOffset"/>/nested
/// generator-ineligible member here - every field is a flat string or the plain
/// <see cref="NotificationChannelType"/> enum.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record NotificationChannelRequest
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public required NotificationChannelType Type { get; init; }

    public string? WebhookUrl { get; init; }

    public string? TelegramBotToken { get; init; }

    public string? TelegramChatId { get; init; }

    public string? EmailTo { get; init; }

    public string? PagerDutyRoutingKey { get; init; }

    /// <summary>See <see cref="NotificationChannel.SendResolved"/>'s doc comment. Omitted/null means true - nullable for the same "omitted vs explicitly false" reason <see cref="AlertRuleRequest.Enabled"/> is.</summary>
    public bool? SendResolved { get; init; }

    public string? JiraBaseUrl { get; init; }

    public string? JiraEmail { get; init; }

    public string? JiraApiToken { get; init; }

    public string? JiraProjectKey { get; init; }

    public string? JiraIssueType { get; init; }

    /// <summary>
    /// Requires exactly the destination field(s) matching <see cref="Type"/> to be set,
    /// and none of the others - the <see cref="NotificationChannel"/> counterpart to
    /// <see cref="AlertRuleRequest.ValidateChannel"/>, adapted to a single explicit
    /// <see cref="Type"/> instead of inferring the channel from whichever field is set
    /// (a standalone channel names its type up front; a legacy rule inline field never
    /// did). Called by <c>NotificationChannelEndpoints</c>'s create/update handlers.
    /// Returns an error message, or null when this request is valid.
    /// </summary>
    public string? ValidateDestination()
    {
        var hasWebhook = !string.IsNullOrWhiteSpace(WebhookUrl);
        var hasBotToken = !string.IsNullOrWhiteSpace(TelegramBotToken);
        var hasChatId = !string.IsNullOrWhiteSpace(TelegramChatId);
        var hasEmail = !string.IsNullOrWhiteSpace(EmailTo);
        var hasPagerDuty = !string.IsNullOrWhiteSpace(PagerDutyRoutingKey);
        var hasJiraField = !string.IsNullOrWhiteSpace(JiraBaseUrl) || !string.IsNullOrWhiteSpace(JiraEmail) || !string.IsNullOrWhiteSpace(JiraApiToken)
            || !string.IsNullOrWhiteSpace(JiraProjectKey) || !string.IsNullOrWhiteSpace(JiraIssueType);

        return Type switch
        {
            NotificationChannelType.Jira when string.IsNullOrWhiteSpace(JiraBaseUrl) || string.IsNullOrWhiteSpace(JiraEmail) || string.IsNullOrWhiteSpace(JiraApiToken) || string.IsNullOrWhiteSpace(JiraProjectKey)
                => "jiraBaseUrl, jiraEmail, jiraApiToken and jiraProjectKey are all required when type is Jira.",
            NotificationChannelType.Jira when !Uri.TryCreate(JiraBaseUrl, UriKind.Absolute, out var jiraUri) || jiraUri.Scheme is not ("https" or "http")
                => "jiraBaseUrl must be an absolute http(s) URL.",
            NotificationChannelType.Jira when hasWebhook || hasBotToken || hasChatId || hasEmail || hasPagerDuty => "Only the jira* fields may be set when type is Jira.",
            NotificationChannelType.Jira => null,
            _ when hasJiraField => $"The jira* fields may only be set when type is Jira.",
            NotificationChannelType.Teams or NotificationChannelType.Discord when !hasWebhook => $"webhookUrl is required when type is {Type}.",
            NotificationChannelType.Teams or NotificationChannelType.Discord when hasBotToken || hasChatId || hasEmail || hasPagerDuty => $"Only webhookUrl may be set when type is {Type}.",
            NotificationChannelType.Webhook when !hasWebhook => "webhookUrl is required when type is Webhook.",
            NotificationChannelType.Webhook when hasBotToken || hasChatId || hasEmail || hasPagerDuty => "Only webhookUrl may be set when type is Webhook.",
            NotificationChannelType.Telegram when !hasBotToken || !hasChatId => "telegramBotToken and telegramChatId are both required when type is Telegram.",
            NotificationChannelType.Telegram when hasWebhook || hasEmail || hasPagerDuty => "Only telegramBotToken/telegramChatId may be set when type is Telegram.",
            NotificationChannelType.Email when !hasEmail => "emailTo is required when type is Email.",
            NotificationChannelType.Email when hasWebhook || hasBotToken || hasChatId || hasPagerDuty => "Only emailTo may be set when type is Email.",
            NotificationChannelType.PagerDuty when !hasPagerDuty => "pagerDutyRoutingKey is required when type is PagerDuty.",
            NotificationChannelType.PagerDuty when hasWebhook || hasBotToken || hasChatId || hasEmail => "Only pagerDutyRoutingKey may be set when type is PagerDuty.",
            _ => null,
        };
    }
}

/// <summary>Response body for <c>GET /api/notification-channels</c>.</summary>
[MemoryPackable]
public sealed partial record NotificationChannelListResponse
{
    public required IReadOnlyList<NotificationChannel> Channels { get; init; }
}

/// <summary>
/// One channel's outcome within a fan-out fire - <see cref="AlertHistoryEntry.ChannelResults"/>'s
/// element type. <see cref="ChannelId"/> is null for a fire through a legacy inline
/// channel (never a saved, named <see cref="NotificationChannel"/>) - <see cref="ChannelName"/>
/// still carries a human-readable label ("Webhook"/"Telegram"/"Email"/"PagerDuty") in that
/// case, via <c>NotificationChannelResolver</c>. Carries <c>[GenerateTypeScript]</c>
/// directly, same reasoning as <see cref="NotificationChannelRequest"/> - flat fields only.
/// </summary>
[MemoryPackable]
[GenerateTypeScript]
public sealed partial record AlertChannelResult
{
    public Guid? ChannelId { get; init; }

    public required string ChannelName { get; init; }

    public required NotificationChannelType Type { get; init; }

    public required bool Success { get; init; }

    public int StatusCode { get; init; }

    public string Error { get; init; } = "";
}
