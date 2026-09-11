using MemoryPack;

namespace Flare.Api.Model;

/// <summary>
/// Which of the four channels a <see cref="NotificationChannel"/> sends through - the
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

        return Type switch
        {
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
