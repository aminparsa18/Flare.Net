using Flare.Api.Alerting;
using Flare.Api.Model;
using Flare.Api.Tests.TestSupport;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>
/// Covers <see cref="NotificationChannelResolver"/> - which channels a rule fans out to,
/// resolved from either its <see cref="AlertRule.ChannelIds"/> or (when that list is
/// empty) its legacy inline fields, per the coexistence design in
/// <c>docs-internal/adr/0021-reusable-notification-channels.md</c>.
/// </summary>
public class NotificationChannelResolverTests
{
    [Fact]
    public async Task ChannelIdsSet_ResolvesFromStore()
    {
        var store = new FakeNotificationChannelQueryService();
        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(),
            Name = "Slack",
            Type = NotificationChannelType.Webhook,
            WebhookUrl = "https://hooks.slack.com/services/x",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        store.Seed(channel);
        var rule = BuildRule(channelIds: [channel.Id]);

        var resolved = await NotificationChannelResolver.ResolveAsync(rule, store, CancellationToken.None);

        var single = Assert.Single(resolved);
        Assert.Equal(channel.Id, single.Id);
    }

    [Fact]
    public async Task ChannelIdsSet_DropsIdsThatNoLongerResolve()
    {
        var store = new FakeNotificationChannelQueryService();
        var rule = BuildRule(channelIds: [Guid.NewGuid()]);

        var resolved = await NotificationChannelResolver.ResolveAsync(rule, store, CancellationToken.None);

        Assert.Empty(resolved);
    }

    [Fact]
    public async Task NoChannelIds_FallsBackToLegacyWebhook()
    {
        var store = new FakeNotificationChannelQueryService();
        var rule = BuildRule(webhookUrl: "https://hooks.slack.com/services/x");

        var resolved = await NotificationChannelResolver.ResolveAsync(rule, store, CancellationToken.None);

        var single = Assert.Single(resolved);
        Assert.Equal(NotificationChannelResolver.LegacyChannelId, single.Id);
        Assert.Equal(NotificationChannelType.Webhook, single.Type);
        Assert.Equal("https://hooks.slack.com/services/x", single.WebhookUrl);
    }

    [Fact]
    public async Task NoChannelIdsAndNoLegacyField_ResolvesEmpty()
    {
        var store = new FakeNotificationChannelQueryService();
        var rule = BuildRule();

        var resolved = await NotificationChannelResolver.ResolveAsync(rule, store, CancellationToken.None);

        Assert.Empty(resolved);
    }

    [Theory]
    [InlineData(NotificationChannelType.Telegram)]
    [InlineData(NotificationChannelType.Email)]
    [InlineData(NotificationChannelType.PagerDuty)]
    public void LegacyChannel_PicksTheOneSetLegacyField(NotificationChannelType expectedType)
    {
        var rule = expectedType switch
        {
            NotificationChannelType.Telegram => BuildRule(telegramBotToken: "123:abc", telegramChatId: "-100"),
            NotificationChannelType.Email => BuildRule(emailTo: "oncall@example.com"),
            NotificationChannelType.PagerDuty => BuildRule(pagerDutyRoutingKey: "R0123456789ABCDEF0123456789ABCDE"),
            _ => throw new ArgumentOutOfRangeException(nameof(expectedType)),
        };

        var channel = NotificationChannelResolver.LegacyChannel(rule);

        Assert.NotNull(channel);
        Assert.Equal(expectedType, channel.Type);
    }

    private static AlertRule BuildRule(
        string webhookUrl = "",
        string telegramBotToken = "",
        string telegramChatId = "",
        string emailTo = "",
        string pagerDutyRoutingKey = "",
        IReadOnlyList<Guid>? channelIds = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new AlertRule
        {
            Id = Guid.NewGuid(),
            Name = "test rule",
            Condition = new LogFilter(),
            Threshold = new AlertThreshold { Count = 1 },
            WindowSeconds = 300,
            WebhookUrl = webhookUrl,
            TelegramBotToken = telegramBotToken,
            TelegramChatId = telegramChatId,
            EmailTo = emailTo,
            PagerDutyRoutingKey = pagerDutyRoutingKey,
            CreatedAt = now,
            UpdatedAt = now,
            ChannelIds = channelIds ?? [],
        };
    }
}
