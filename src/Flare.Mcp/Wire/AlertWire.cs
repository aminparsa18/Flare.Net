using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flare.Mcp;

internal sealed class AlertThresholdWire
{
    public required ulong Count { get; init; }

    /// <summary>"GreaterThanOrEqual" | "LessThan".</summary>
    public string Comparator { get; init; } = "GreaterThanOrEqual";
}

internal sealed class AlertRuleWire
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public bool Enabled { get; init; } = true;

    public required AlertThresholdWire Threshold { get; init; }

    public required int WindowSeconds { get; init; }

    public int CooldownSeconds { get; init; } = 300;

    public string WebhookUrl { get; init; } = "";

    public string TelegramBotToken { get; init; } = "";

    public string TelegramChatId { get; init; } = "";

    public string EmailTo { get; init; } = "";

    public string PagerDutyRoutingKey { get; init; } = "";

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    /// <summary>See <c>Flare.Api.Model.AlertRule.ChannelIds</c>'s doc comment - saved notification-channel IDs this rule fans out to, instead of one of the legacy inline fields above. <see cref="AlertsListCommand"/> still only shows the count (see <c>DescribeChannel</c>), not each channel's name - resolving IDs to names via <c>flare notification-channels list</c> (<c>NotificationChannelsCommand.cs</c>) remains a named follow-up.</summary>
    public IReadOnlyList<Guid> ChannelIds { get; init; } = [];

    /// <summary>"LogCount" | "MetricThreshold" | "ExceptionCount" | "Anomaly". Absent from older servers, where it reads as "LogCount".</summary>
    public string ConditionKind { get; init; } = "LogCount";

    /// <summary>Set only when <see cref="ConditionKind"/> is "Anomaly" - see <c>Flare.Api.Model.AnomalyCondition</c>.</summary>
    public AnomalyConditionWire? AnomalyCondition { get; init; }
}

internal sealed class AnomalyConditionWire
{
    /// <summary>"Daily" | "Weekly".</summary>
    public string Seasonality { get; init; } = "Daily";

    public int BaselinePeriods { get; init; }

    public double ZScoreThreshold { get; init; }

    /// <summary>"Both" | "Above" | "Below".</summary>
    public string Direction { get; init; } = "Both";
}

internal sealed class AlertRuleListResponseWire
{
    public List<AlertRuleWire> Rules { get; init; } = [];
}
