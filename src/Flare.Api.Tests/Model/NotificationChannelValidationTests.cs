using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Model;

/// <summary>
/// Covers <see cref="NotificationChannelRequest.ValidateDestination"/> - the
/// <see cref="NotificationChannel"/> counterpart to
/// <see cref="AlertChannelValidationTests"/>/<see cref="AlertRuleRequest.ValidateChannel"/>,
/// adapted to a single explicit <see cref="NotificationChannelType"/> instead of inferring
/// the channel from whichever field is set.
/// </summary>
public class NotificationChannelValidationTests
{
    [Fact]
    public void Webhook_WithWebhookUrl_IsValid()
    {
        var request = Build(NotificationChannelType.Webhook, webhookUrl: "https://hooks.slack.com/services/x");

        Assert.Null(request.ValidateDestination());
    }

    [Fact]
    public void Webhook_WithoutWebhookUrl_IsInvalid()
    {
        var request = Build(NotificationChannelType.Webhook);

        Assert.NotNull(request.ValidateDestination());
    }

    [Fact]
    public void Webhook_WithExtraFieldSet_IsInvalid()
    {
        var request = Build(NotificationChannelType.Webhook, webhookUrl: "https://hooks.slack.com/services/x", emailTo: "oncall@example.com");

        Assert.NotNull(request.ValidateDestination());
    }

    [Fact]
    public void Telegram_WithBothFields_IsValid()
    {
        var request = Build(NotificationChannelType.Telegram, telegramBotToken: "123:abc", telegramChatId: "-100");

        Assert.Null(request.ValidateDestination());
    }

    [Theory]
    [InlineData("123:abc", "")]
    [InlineData("", "-100")]
    public void Telegram_WithOnlyOneField_IsInvalid(string botToken, string chatId)
    {
        var request = Build(NotificationChannelType.Telegram, telegramBotToken: botToken, telegramChatId: chatId);

        Assert.NotNull(request.ValidateDestination());
    }

    [Fact]
    public void Email_WithEmailTo_IsValid()
    {
        var request = Build(NotificationChannelType.Email, emailTo: "oncall@example.com");

        Assert.Null(request.ValidateDestination());
    }

    [Fact]
    public void Email_WithoutEmailTo_IsInvalid()
    {
        var request = Build(NotificationChannelType.Email);

        Assert.NotNull(request.ValidateDestination());
    }

    [Fact]
    public void PagerDuty_WithRoutingKey_IsValid()
    {
        var request = Build(NotificationChannelType.PagerDuty, pagerDutyRoutingKey: "R0123456789ABCDEF0123456789ABCDE");

        Assert.Null(request.ValidateDestination());
    }

    [Fact]
    public void PagerDuty_WithoutRoutingKey_IsInvalid()
    {
        var request = Build(NotificationChannelType.PagerDuty);

        Assert.NotNull(request.ValidateDestination());
    }

    [Theory]
    [InlineData(NotificationChannelType.Teams)]
    [InlineData(NotificationChannelType.Discord)]
    public void WebhookBasedTypes_WithWebhookUrl_AreValid(NotificationChannelType type)
    {
        Assert.Null(Build(type, webhookUrl: "https://example.com/hook").ValidateDestination());
    }

    [Theory]
    [InlineData(NotificationChannelType.Teams)]
    [InlineData(NotificationChannelType.Discord)]
    public void WebhookBasedTypes_WithoutWebhookUrl_AreInvalid(NotificationChannelType type)
    {
        Assert.NotNull(Build(type).ValidateDestination());
    }

    [Theory]
    [InlineData(NotificationChannelType.Teams)]
    [InlineData(NotificationChannelType.Discord)]
    public void WebhookBasedTypes_WithAnotherDestination_AreInvalid(NotificationChannelType type)
    {
        Assert.NotNull(Build(type, webhookUrl: "https://example.com/hook", emailTo: "oncall@example.com").ValidateDestination());
    }

    [Fact]
    public void Jira_WithAllRequiredFields_IsValid()
    {
        Assert.Null(Jira().ValidateDestination());
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://acme.atlassian.net")]
    public void Jira_WithMissingOrMalformedBaseUrl_IsInvalid(string baseUrl)
    {
        Assert.NotNull(Jira(baseUrl: baseUrl).ValidateDestination());
    }

    [Fact]
    public void Jira_WithMissingToken_IsInvalid()
    {
        Assert.NotNull(Jira(token: "").ValidateDestination());
    }

    [Fact]
    public void Jira_WithAnotherDestination_IsInvalid()
    {
        var request = Jira() with { WebhookUrl = "https://example.com/hook" };
        Assert.NotNull(request.ValidateDestination());
    }

    [Fact]
    public void JiraFields_OnAnotherType_AreInvalid()
    {
        var request = Build(NotificationChannelType.Webhook, webhookUrl: "https://example.com/hook") with { JiraProjectKey = "OPS" };
        Assert.NotNull(request.ValidateDestination());
    }

    [Fact]
    public void IncidentIo_WithUrlAndToken_IsValid()
    {
        Assert.Null(IncidentIo().ValidateDestination());
    }

    [Fact]
    public void IncidentIo_WithoutToken_IsInvalid()
    {
        Assert.NotNull((IncidentIo() with { IncidentIoToken = "" }).ValidateDestination());
    }

    [Fact]
    public void IncidentIo_WithAnotherDestination_IsInvalid()
    {
        Assert.NotNull((IncidentIo() with { EmailTo = "a@b.com" }).ValidateDestination());
    }

    [Fact]
    public void IncidentIoToken_OnAnotherType_IsInvalid()
    {
        var request = Build(NotificationChannelType.Webhook, webhookUrl: "https://example.com/hook") with { IncidentIoToken = "t" };
        Assert.NotNull(request.ValidateDestination());
    }

    private static NotificationChannelRequest IncidentIo() => new()
    {
        Name = "test",
        Type = NotificationChannelType.IncidentIo,
        WebhookUrl = "https://api.incident.io/v2/alert_events/http/abc",
        IncidentIoToken = "tok",
    };

    private static NotificationChannelRequest Jira(string baseUrl = "https://acme.atlassian.net", string token = "tok") => new()
    {
        Name = "test",
        Type = NotificationChannelType.Jira,
        JiraBaseUrl = baseUrl,
        JiraEmail = "bot@acme.com",
        JiraApiToken = token,
        JiraProjectKey = "OPS",
    };

    private static NotificationChannelRequest Build(
        NotificationChannelType type,
        string webhookUrl = "",
        string telegramBotToken = "",
        string telegramChatId = "",
        string emailTo = "",
        string pagerDutyRoutingKey = "") => new()
    {
        Name = "test",
        Type = type,
        WebhookUrl = webhookUrl,
        TelegramBotToken = telegramBotToken,
        TelegramChatId = telegramChatId,
        EmailTo = emailTo,
        PagerDutyRoutingKey = pagerDutyRoutingKey,
    };
}
