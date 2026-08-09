using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Model;

/// <summary>
/// Covers <see cref="AlertRuleRequest.ValidateChannel"/> - the other piece of pure logic
/// in the alerting feature alongside <see cref="AlertThreshold.IsBreached"/> (see
/// <see cref="AlertThresholdTests"/>'s doc comment for why everything else alerting-related
/// is verified end-to-end instead of unit-tested).
/// </summary>
public class AlertChannelValidationTests
{
    [Fact]
    public void WebhookOnly_IsValid()
    {
        var request = Build(webhookUrl: "https://hooks.slack.com/services/x");

        Assert.Null(request.ValidateChannel());
    }

    [Fact]
    public void TelegramOnly_IsValid()
    {
        var request = Build(telegramBotToken: "123:abc", telegramChatId: "-100");

        Assert.Null(request.ValidateChannel());
    }

    [Fact]
    public void NeitherChannelSet_IsInvalid()
    {
        var request = Build();

        Assert.NotNull(request.ValidateChannel());
    }

    [Fact]
    public void BothChannelsSet_IsInvalid()
    {
        var request = Build(webhookUrl: "https://hooks.slack.com/services/x", telegramBotToken: "123:abc", telegramChatId: "-100");

        Assert.NotNull(request.ValidateChannel());
    }

    [Theory]
    [InlineData("123:abc", "")]
    [InlineData("", "-100")]
    public void OnlyOneTelegramFieldSet_IsInvalid(string botToken, string chatId)
    {
        var request = Build(telegramBotToken: botToken, telegramChatId: chatId);

        Assert.NotNull(request.ValidateChannel());
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public void BlankWebhookUrl_DoesNotCountAsSet(string webhookUrl)
    {
        var request = Build(webhookUrl: webhookUrl, telegramBotToken: "123:abc", telegramChatId: "-100");

        Assert.Null(request.ValidateChannel());
    }

    private static AlertRuleRequest Build(string webhookUrl = "", string telegramBotToken = "", string telegramChatId = "") => new()
    {
        Name = "test",
        Threshold = new AlertThreshold { Count = 1 },
        WindowSeconds = 300,
        WebhookUrl = webhookUrl,
        TelegramBotToken = telegramBotToken,
        TelegramChatId = telegramChatId,
    };
}
