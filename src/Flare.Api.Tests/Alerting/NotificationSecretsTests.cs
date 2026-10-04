using Flare.Api.Alerting;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>
/// Covers <see cref="NotificationSecrets"/>: what a credential looks like once masked for an
/// API response, and that a masked value sent back on update resolves to the stored secret
/// while a changed one replaces it.
/// </summary>
public class NotificationSecretsTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);

    private static NotificationChannel Channel(NotificationChannelType type) => new()
    {
        Id = Guid.NewGuid(),
        Name = "ops",
        Type = type,
        WebhookUrl = "https://hooks.slack.com/services/T000/B000/XXXXXXXXsecretAbcd",
        TelegramBotToken = "123456:AAH-telegram-bot-token-wxyz",
        TelegramChatId = "-100200300",
        EmailTo = "oncall@example.com",
        PagerDutyRoutingKey = "R0UT1NGKEY0123456789qrst",
        JiraBaseUrl = "https://acme.atlassian.net",
        JiraEmail = "bot@acme.com",
        JiraApiToken = "ATATT3xFfGF0-jira-token-mnop",
        IncidentIoToken = "inc-io-bearer-token-ijkl",
        JsmOpsApiKey = "jsm-ops-genie-key-efgh",
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    [Fact]
    public void Mask_Empty_StaysEmpty() => Assert.Equal("", NotificationSecrets.Mask(""));

    [Fact]
    public void Mask_Url_KeepsSchemeAndHostOnly()
    {
        var masked = NotificationSecrets.Mask("https://hooks.slack.com/services/T000/B000/XXXXXXXXsecretAbcd");

        Assert.Equal($"https://hooks.slack.com/{NotificationSecrets.MaskMarker}Abcd", masked);
        Assert.DoesNotContain("services", masked);
    }

    [Fact]
    public void Mask_LongToken_KeepsLastFour() =>
        Assert.Equal($"{NotificationSecrets.MaskMarker}wxyz", NotificationSecrets.Mask("123456:AAH-telegram-bot-token-wxyz"));

    [Fact]
    public void Mask_ShortToken_RevealsNothing() =>
        Assert.Equal(NotificationSecrets.MaskMarker, NotificationSecrets.Mask("short-key"));

    [Fact]
    public void Redact_Channel_MasksEverySecret_KeepsNonSecrets()
    {
        var channel = Channel(NotificationChannelType.Webhook);
        var redacted = NotificationSecrets.Redact(channel);

        Assert.DoesNotContain("XXXXXXXXsecret", redacted.WebhookUrl);
        Assert.StartsWith(NotificationSecrets.MaskMarker, redacted.TelegramBotToken);
        Assert.StartsWith(NotificationSecrets.MaskMarker, redacted.PagerDutyRoutingKey);
        Assert.StartsWith(NotificationSecrets.MaskMarker, redacted.JiraApiToken);
        Assert.StartsWith(NotificationSecrets.MaskMarker, redacted.IncidentIoToken);
        Assert.StartsWith(NotificationSecrets.MaskMarker, redacted.JsmOpsApiKey);

        Assert.Equal(channel.TelegramChatId, redacted.TelegramChatId);
        Assert.Equal(channel.EmailTo, redacted.EmailTo);
        Assert.Equal(channel.JiraBaseUrl, redacted.JiraBaseUrl);
        Assert.Equal(channel.JiraEmail, redacted.JiraEmail);
    }

    [Fact]
    public void Restore_ChannelRequest_MaskedValuesResolveToStored()
    {
        var stored = Channel(NotificationChannelType.IncidentIo);
        var redacted = NotificationSecrets.Redact(stored);
        var request = new NotificationChannelRequest
        {
            Name = "ops",
            Type = NotificationChannelType.IncidentIo,
            WebhookUrl = redacted.WebhookUrl,
            IncidentIoToken = redacted.IncidentIoToken,
        };

        var restored = NotificationSecrets.Restore(request, stored);

        Assert.Equal(stored.WebhookUrl, restored.WebhookUrl);
        Assert.Equal(stored.IncidentIoToken, restored.IncidentIoToken);
        Assert.Null(restored.ValidateDestination());
    }

    [Fact]
    public void Restore_ChannelRequest_ChangedValueReplacesStored()
    {
        var stored = Channel(NotificationChannelType.Telegram);
        var request = new NotificationChannelRequest
        {
            Name = "ops",
            Type = NotificationChannelType.Telegram,
            TelegramBotToken = "999999:new-token-from-botfather",
            TelegramChatId = stored.TelegramChatId,
        };

        Assert.Equal("999999:new-token-from-botfather", NotificationSecrets.Restore(request, stored).TelegramBotToken);
    }

    [Fact]
    public void Restore_WithoutExistingRow_LeavesRequestAsSent()
    {
        var request = new NotificationChannelRequest { Name = "ops", Type = NotificationChannelType.Webhook, WebhookUrl = $"https://x.test/{NotificationSecrets.MaskMarker}" };

        Assert.Same(request, NotificationSecrets.Restore(request, existing: null));
    }

    [Fact]
    public void Restore_ShortSecret_MaskResolvesToStored()
    {
        // A short secret masks to the bare marker - still unambiguous against its own row.
        Assert.Equal("short-key", NotificationSecrets.Restore(NotificationSecrets.MaskMarker, "short-key"));
    }

    [Fact]
    public void Redact_And_Restore_AlertRuleLegacyFields()
    {
        var rule = new AlertRule
        {
            Id = Guid.NewGuid(),
            Name = "errors",
            Condition = new LogFilter(),
            Threshold = new AlertThreshold { Count = 10 },
            WindowSeconds = 300,
            PagerDutyRoutingKey = "R0UT1NGKEY0123456789qrst",
            CreatedAt = Now,
            UpdatedAt = Now,
        };

        var redacted = NotificationSecrets.Redact(rule);
        Assert.Equal($"{NotificationSecrets.MaskMarker}qrst", redacted.PagerDutyRoutingKey);

        var request = new AlertRuleRequest
        {
            Name = rule.Name,
            Condition = rule.Condition,
            Threshold = rule.Threshold,
            WindowSeconds = rule.WindowSeconds,
            PagerDutyRoutingKey = redacted.PagerDutyRoutingKey,
        };
        Assert.Equal(rule.PagerDutyRoutingKey, NotificationSecrets.Restore(request, rule).PagerDutyRoutingKey);
    }
}
