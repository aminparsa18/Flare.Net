namespace Flare.Api.Model;

/// <summary>How <see cref="AlertThreshold.Count"/> is compared against the observed count.</summary>
public enum ThresholdComparator
{
    /// <summary>Fires when the observed count is at or above the threshold (the common "error spike" case).</summary>
    GreaterThanOrEqual,

    /// <summary>Fires when the observed count is below the threshold (e.g. "a service went quiet").</summary>
    LessThan,
}

/// <summary>The breach condition an <see cref="AlertRule"/> evaluates on every poll tick.</summary>
public sealed record AlertThreshold
{
    public required ulong Count { get; init; }

    public ThresholdComparator Comparator { get; init; } = ThresholdComparator.GreaterThanOrEqual;

    /// <summary>
    /// Shared by <c>AlertEvaluationWorker</c> (the real evaluation loop) and the
    /// <c>/api/alerts/{id}/test</c>/<c>/api/alerts/test</c> dry-run endpoints, so "would
    /// this fire" always means the same thing whether asked live or in a test.
    /// </summary>
    public bool IsBreached(ulong observedCount) => Comparator switch
    {
        ThresholdComparator.LessThan => observedCount < Count,
        _ => observedCount >= Count,
    };
}

/// <summary>A saved threshold/query-based alert rule.</summary>
/// <remarks>
/// <see cref="Condition"/> reuses <see cref="LogFilter"/> verbatim - the same filter DSL
/// <c>/api/logs/search</c>/<c>/api/logs/aggregate</c> already compile via
/// <see cref="Query.LogFilterSqlBuilder"/>. Its <see cref="LogFilter.From"/>/
/// <see cref="LogFilter.To"/> are ignored for alert evaluation:
/// <c>AlertEvaluationWorker</c> supplies its own rolling window derived from
/// <see cref="WindowSeconds"/> at evaluation time.
/// </remarks>
public sealed record AlertRule
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public bool Enabled { get; init; } = true;

    public required LogFilter Condition { get; init; }

    public required AlertThreshold Threshold { get; init; }

    /// <summary>Rolling window, in seconds, the threshold is evaluated over on every poll tick.</summary>
    public required int WindowSeconds { get; init; }

    /// <summary>Minimum seconds between two notifications for this rule, even if it keeps breaching.</summary>
    public int CooldownSeconds { get; init; } = 300;

    /// <summary>
    /// Where the fired-alert JSON payload is POSTed. Covers both a generic webhook
    /// consumer and a Slack incoming-webhook URL - see <c>WebhookAlertNotifier</c> for
    /// the shared payload shape. Mutually exclusive with <see cref="TelegramBotToken"/>/
    /// <see cref="TelegramChatId"/> - a rule notifies exactly one channel; see
    /// <c>AlertEndpoints</c>'s channel validation.
    /// </summary>
    public string WebhookUrl { get; init; } = "";

    /// <summary>
    /// The Telegram bot (from @BotFather) a fired alert is sent through, when this rule's
    /// channel is Telegram instead of webhook/Slack. Set together with
    /// <see cref="TelegramChatId"/>, never alongside <see cref="WebhookUrl"/> - see
    /// <c>TelegramAlertNotifier</c>.
    /// </summary>
    public string TelegramBotToken { get; init; } = "";

    /// <summary>The target chat/channel/group ID <see cref="TelegramBotToken"/> sends the fired-alert message to.</summary>
    public string TelegramChatId { get; init; } = "";

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>Create/update request body for <c>/api/alerts</c>.</summary>
/// <remarks>
/// Every optional member below is nullable rather than typed with a non-default C#
/// initializer, unlike <see cref="AlertRule"/>'s equivalents - see
/// <see cref="Model.LogSearchRequest.Filter"/>'s doc comment for the general caveat, and
/// why it hits harder here: this type also has `required` members, which pushes System.Text.Json's
/// source generator onto its parameterized-constructor-style converter (confirmed live -
/// a JSON body omitting a `required` member throws instead of silently defaulting, proving
/// this converter treats every settable member, not just the `required` ones, as a
/// constructor parameter and passes <see langword="default"/> for anything the JSON body
/// omits). For <see cref="bool"/>/<see cref="int"/> members that collision is
/// unrecoverable in place: a caller must be able to send <c>"enabled":false</c>
/// explicitly, so a non-nullable <see cref="bool"/> can never tell "omitted" apart from
/// "explicitly false" once <see langword="default"/> and "explicitly false" are the same
/// value. Hence <see cref="bool"/>?/<see cref="int"/>? here, coalesced to the real default
/// in <see cref="Query.AlertQueryService"/>'s <c>CreateAsync</c>/<c>UpdateAsync</c> - same
/// pattern <see cref="Query.LogSearchQueryBuilder.Build"/> uses for <see cref="Model.LogSearchRequest.Filter"/>.
/// The <see cref="string"/> members have no such collision (there's no meaningful
/// difference between "omitted" and "explicitly empty" for a description or webhook URL)
/// but are made nullable too, so the whole type uses one consistent shape instead of
/// mixing coalescing strategies.
/// </remarks>
public sealed record AlertRuleRequest
{
    public required string Name { get; init; }

    public string? Description { get; init; }

    public bool? Enabled { get; init; }

    /// <summary>See <see cref="Model.LogSearchRequest.Filter"/>'s doc comment - the same JSON-deserialization default caveat applies here.</summary>
    public LogFilter Condition { get; init; } = new();

    public required AlertThreshold Threshold { get; init; }

    public required int WindowSeconds { get; init; }

    public int? CooldownSeconds { get; init; }

    /// <summary>See <see cref="AlertRule.WebhookUrl"/>'s doc comment - mutually exclusive with <see cref="TelegramBotToken"/>/<see cref="TelegramChatId"/>.</summary>
    public string? WebhookUrl { get; init; }

    /// <summary>See <see cref="AlertRule.TelegramBotToken"/>'s doc comment.</summary>
    public string? TelegramBotToken { get; init; }

    /// <summary>See <see cref="AlertRule.TelegramChatId"/>'s doc comment.</summary>
    public string? TelegramChatId { get; init; }

    /// <summary>
    /// Exactly one notification channel: either <see cref="WebhookUrl"/> (covers both a
    /// generic webhook consumer and Slack), or both <see cref="TelegramBotToken"/> and
    /// <see cref="TelegramChatId"/> together - never neither, never both channels, never
    /// one Telegram field without the other. Called by <c>AlertEndpoints</c>'s
    /// create/update handlers (not the dry-run test endpoints, which never notify).
    /// Returns an error message, or null when this request is valid.
    /// </summary>
    public string? ValidateChannel()
    {
        var hasWebhook = !string.IsNullOrWhiteSpace(WebhookUrl);
        var hasBotToken = !string.IsNullOrWhiteSpace(TelegramBotToken);
        var hasChatId = !string.IsNullOrWhiteSpace(TelegramChatId);
        var hasTelegram = hasBotToken && hasChatId;

        if (hasBotToken != hasChatId)
        {
            return "telegramBotToken and telegramChatId must be set together.";
        }

        return (hasWebhook, hasTelegram) switch
        {
            (false, false) => "Either webhookUrl or telegramBotToken/telegramChatId is required.",
            (true, true) => "webhookUrl and telegramBotToken/telegramChatId are mutually exclusive - a rule notifies exactly one channel.",
            _ => null,
        };
    }
}

/// <summary>Response body for <c>GET /api/alerts</c>.</summary>
public sealed record AlertRuleListResponse
{
    public required IReadOnlyList<AlertRule> Rules { get; init; }
}

/// <summary>One row of an <see cref="AlertRule"/>'s fired-notification history.</summary>
public sealed record AlertHistoryEntry
{
    public required Guid EventId { get; init; }

    public required Guid RuleId { get; init; }

    /// <summary>Snapshot of the rule's name at fire time - survives a later rename/delete.</summary>
    public required string RuleName { get; init; }

    public required DateTimeOffset FiredAt { get; init; }

    public required ulong ObservedCount { get; init; }

    public required ulong ThresholdCount { get; init; }

    public required int WindowSeconds { get; init; }

    /// <summary>"Sent" | "Failed".</summary>
    public required string NotificationStatus { get; init; }

    /// <summary>Webhook response HTTP status; 0 if the POST never completed.</summary>
    public int NotificationStatusCode { get; init; }

    public string NotificationError { get; init; } = "";
}

/// <summary>Response body for <c>GET /api/alerts/{id}/history</c>.</summary>
public sealed record AlertHistoryResponse
{
    public required IReadOnlyList<AlertHistoryEntry> Events { get; init; }
}

/// <summary>
/// Response body for the dry-run test endpoints (<c>POST /api/alerts/{id}/test</c> and
/// <c>POST /api/alerts/test</c>) - evaluates the condition/threshold against current
/// data without touching cooldown state or sending a notification.
/// </summary>
public sealed record AlertTestResult
{
    public required ulong ObservedCount { get; init; }

    public required bool WouldFire { get; init; }

    public required DateTimeOffset EvaluatedAt { get; init; }

    public required int WindowSeconds { get; init; }
}
