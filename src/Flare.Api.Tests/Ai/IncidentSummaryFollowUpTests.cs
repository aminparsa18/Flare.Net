using Flare.Api.Ai;
using Flare.Api.Alerting;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Ai;

public class IncidentSummaryFollowUpTests
{
    private static NotificationChannel Channel(NotificationChannelType type, string webhookUrl = "") => new()
    {
        Id = Guid.NewGuid(),
        Name = "c",
        Type = type,
        WebhookUrl = webhookUrl,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    [Theory]
    [InlineData(NotificationChannelType.Telegram, true)]
    [InlineData(NotificationChannelType.Teams, true)]
    [InlineData(NotificationChannelType.Discord, true)]
    [InlineData(NotificationChannelType.Email, true)]
    [InlineData(NotificationChannelType.PagerDuty, false)]
    [InlineData(NotificationChannelType.Jira, false)]
    [InlineData(NotificationChannelType.IncidentIo, false)]
    [InlineData(NotificationChannelType.JsmOps, false)]
    public void IsEligible_ByChannelType(NotificationChannelType type, bool expected) =>
        Assert.Equal(expected, IncidentSummaryFollowUp.IsEligible(Channel(type)));

    [Fact]
    public void IsEligible_Webhook_OnlyForSlack()
    {
        Assert.True(IncidentSummaryFollowUp.IsEligible(Channel(NotificationChannelType.Webhook, "https://hooks.slack.com/services/a/b")));
        Assert.False(IncidentSummaryFollowUp.IsEligible(Channel(NotificationChannelType.Webhook, "https://automation.example.com/hook")));
    }

    [Fact]
    public void BuildRule_PutsSummaryInTheTemplatesWithoutPlaceholders()
    {
        var rule = new AlertRule
        {
            Id = Guid.NewGuid(),
            Name = "Errors {{rule_url}}",
            Condition = new LogFilter(),
            Threshold = new AlertThreshold { Count = 1 },
            WindowSeconds = 60,
            CreatedAt = DateTimeOffset.UnixEpoch,
            UpdatedAt = DateTimeOffset.UnixEpoch,
        };

        var followUp = IncidentSummaryFollowUp.BuildRule(rule, "Cause: {{message}} leaked");
        var message = AlertMessageFormatter.BuildMessage(followUp, 5, false, null, null, DateTimeOffset.UnixEpoch, false, null);

        Assert.StartsWith(IncidentSummaryFollowUp.TitlePrefix + "Errors ", message.Title);
        Assert.Contains("{ {rule_url}}", message.Title);
        Assert.Contains("Cause: { {message}} leaked", message.Text);
    }
}
