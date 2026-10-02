using System.Net;
using System.Text.Json;
using Flare.Api.Alerting;
using Flare.Api.Model;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>
/// Covers the Events API v2 payload <see cref="PagerDutyAlertNotifier"/> builds - the
/// <c>dedup_key</c> a rule's triggers and resolve share, which is what makes a recovery
/// auto-close the incident (ADR-0064). PagerDuty itself can't be reached from a test, so an
/// in-process handler captures the request body instead of sending it anywhere.
/// </summary>
public class PagerDutyAlertNotifierTests
{
    private static readonly AlertRule Rule = new()
    {
        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Name = "High error rate",
        Condition = new LogFilter(),
        Threshold = new AlertThreshold { Count = 10 },
        WindowSeconds = 60,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private static readonly NotificationChannel Channel = new()
    {
        Id = Guid.NewGuid(),
        Name = "On-call",
        Type = NotificationChannelType.PagerDuty,
        PagerDutyRoutingKey = "routing-key",
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private static async Task<JsonElement> SendAsync(bool isTest = false, bool resolved = false, AlertRule? rule = null)
    {
        var handler = new CapturingHandler();
        var notifier = new PagerDutyAlertNotifier(new HttpClient(handler), Options.Create(new AlertLinkOptions()));

        var result = await notifier.SendAsync(rule ?? Rule, Channel, 42, DateTimeOffset.UnixEpoch, CancellationToken.None, isTest: isTest, resolved: resolved);

        Assert.True(result.Success);
        return JsonDocument.Parse(handler.Body!).RootElement;
    }

    [Theory]
    [InlineData(AlertSeverity.Critical, "critical")]
    [InlineData(AlertSeverity.Error, "error")]
    [InlineData(AlertSeverity.Warning, "warning")]
    [InlineData(AlertSeverity.Info, "info")]
    public async Task Trigger_MapsRuleSeverityToPagerDutySeverity(AlertSeverity severity, string expected)
    {
        var body = await SendAsync(rule: Rule with { Severity = severity });

        Assert.Equal(expected, body.GetProperty("payload").GetProperty("severity").GetString());
    }

    [Fact]
    public async Task TestSend_IsAlwaysInfoSeverity()
    {
        var body = await SendAsync(isTest: true, rule: Rule with { Severity = AlertSeverity.Critical });

        Assert.Equal("info", body.GetProperty("payload").GetProperty("severity").GetString());
    }

    [Fact]
    public async Task Trigger_CarriesThePerRuleDedupKey()
    {
        var body = await SendAsync();

        Assert.Equal("trigger", body.GetProperty("event_action").GetString());
        Assert.Equal("flare-alert-11111111222233334444555555555555", body.GetProperty("dedup_key").GetString());
    }

    [Fact]
    public async Task Resolve_SendsResolveWithTheSameDedupKey()
    {
        var body = await SendAsync(resolved: true);

        Assert.Equal("resolve", body.GetProperty("event_action").GetString());
        Assert.Equal(PagerDutyAlertNotifier.DedupKey(Rule), body.GetProperty("dedup_key").GetString());
        Assert.Equal("routing-key", body.GetProperty("routing_key").GetString());
    }

    [Fact]
    public async Task TestSend_NeverSharesTheRulesDedupKey()
    {
        var body = await SendAsync(isTest: true);

        Assert.Equal("trigger", body.GetProperty("event_action").GetString());
        Assert.NotEqual(PagerDutyAlertNotifier.DedupKey(Rule), body.GetProperty("dedup_key").GetString());
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }
}
