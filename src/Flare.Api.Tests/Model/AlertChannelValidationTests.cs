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
    public void EmailOnly_IsValid()
    {
        var request = Build(emailTo: "oncall@example.com");

        Assert.Null(request.ValidateChannel());
    }

    [Fact]
    public void PagerDutyOnly_IsValid()
    {
        var request = Build(pagerDutyRoutingKey: "R0123456789ABCDEF0123456789ABCDE");

        Assert.Null(request.ValidateChannel());
    }

    [Fact]
    public void NoChannelSet_IsInvalid()
    {
        var request = Build();

        Assert.NotNull(request.ValidateChannel());
    }

    [Fact]
    public void WebhookAndTelegramSet_IsInvalid()
    {
        var request = Build(webhookUrl: "https://hooks.slack.com/services/x", telegramBotToken: "123:abc", telegramChatId: "-100");

        Assert.NotNull(request.ValidateChannel());
    }

    [Fact]
    public void WebhookAndEmailSet_IsInvalid()
    {
        var request = Build(webhookUrl: "https://hooks.slack.com/services/x", emailTo: "oncall@example.com");

        Assert.NotNull(request.ValidateChannel());
    }

    [Fact]
    public void TelegramAndEmailSet_IsInvalid()
    {
        var request = Build(telegramBotToken: "123:abc", telegramChatId: "-100", emailTo: "oncall@example.com");

        Assert.NotNull(request.ValidateChannel());
    }

    [Fact]
    public void EmailAndPagerDutySet_IsInvalid()
    {
        var request = Build(emailTo: "oncall@example.com", pagerDutyRoutingKey: "R0123456789ABCDEF0123456789ABCDE");

        Assert.NotNull(request.ValidateChannel());
    }

    [Fact]
    public void AllFourChannelsSet_IsInvalid()
    {
        var request = Build(
            webhookUrl: "https://hooks.slack.com/services/x",
            telegramBotToken: "123:abc",
            telegramChatId: "-100",
            emailTo: "oncall@example.com",
            pagerDutyRoutingKey: "R0123456789ABCDEF0123456789ABCDE");

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

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public void BlankEmailTo_DoesNotCountAsSet(string emailTo)
    {
        var request = Build(webhookUrl: "https://hooks.slack.com/services/x", emailTo: emailTo);

        Assert.Null(request.ValidateChannel());
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("")]
    public void BlankPagerDutyRoutingKey_DoesNotCountAsSet(string pagerDutyRoutingKey)
    {
        var request = Build(webhookUrl: "https://hooks.slack.com/services/x", pagerDutyRoutingKey: pagerDutyRoutingKey);

        Assert.Null(request.ValidateChannel());
    }

    // --- ChannelIds (the reusable-channel counterpart to the legacy inline fields above) ---

    [Fact]
    public void ChannelIdsOnly_IsValid()
    {
        var request = Build(channelIds: [Guid.NewGuid()]);

        Assert.Null(request.ValidateChannel());
    }

    [Fact]
    public void MultipleChannelIds_IsValid()
    {
        var request = Build(channelIds: [Guid.NewGuid(), Guid.NewGuid()]);

        Assert.Null(request.ValidateChannel());
    }

    [Fact]
    public void EmptyChannelIdsAndNoLegacyChannel_IsInvalid()
    {
        var request = Build(channelIds: []);

        Assert.NotNull(request.ValidateChannel());
    }

    [Fact]
    public void ChannelIdsAndWebhookBothSet_IsInvalid()
    {
        var request = Build(webhookUrl: "https://hooks.slack.com/services/x", channelIds: [Guid.NewGuid()]);

        Assert.NotNull(request.ValidateChannel());
    }

    [Fact]
    public void ChannelIdsAndPagerDutyBothSet_IsInvalid()
    {
        var request = Build(pagerDutyRoutingKey: "R0123456789ABCDEF0123456789ABCDE", channelIds: [Guid.NewGuid()]);

        Assert.NotNull(request.ValidateChannel());
    }

    private static AlertRuleRequest Build(
        string webhookUrl = "",
        string telegramBotToken = "",
        string telegramChatId = "",
        string emailTo = "",
        string pagerDutyRoutingKey = "",
        IReadOnlyList<Guid>? channelIds = null) => new()
    {
        Name = "test",
        Threshold = new AlertThreshold { Count = 1 },
        WindowSeconds = 300,
        WebhookUrl = webhookUrl,
        TelegramBotToken = telegramBotToken,
        TelegramChatId = telegramChatId,
        EmailTo = emailTo,
        PagerDutyRoutingKey = pagerDutyRoutingKey,
        ChannelIds = channelIds,
    };
}
