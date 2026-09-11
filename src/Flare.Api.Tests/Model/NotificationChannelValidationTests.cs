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
