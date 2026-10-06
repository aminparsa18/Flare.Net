using System.Security.Cryptography;
using System.Text;
using Flare.Api.Alerting;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>Covers <see cref="InboundAckSignatures"/> and the pure parts of the Slack/PagerDuty ack integrations (ADR-0138).</summary>
public class InboundAckSignaturesTests
{
    private const string Secret = "s3cret";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000);

    private static string Hex(string data) => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(data)));

    private static string SlackSig(string ts, string body) => $"v0={Hex($"v0:{ts}:{body}")}";

    [Fact]
    public void Slack_accepts_a_correctly_signed_fresh_request()
    {
        var ts = Now.ToUnixTimeSeconds().ToString();
        Assert.True(InboundAckSignatures.IsValidSlack(Secret, ts, SlackSig(ts, "payload=x"), "payload=x", Now));
    }

    [Fact]
    public void Slack_rejects_a_wrong_secret_a_tampered_body_and_a_missing_signature()
    {
        var ts = Now.ToUnixTimeSeconds().ToString();
        var sig = SlackSig(ts, "payload=x");
        Assert.False(InboundAckSignatures.IsValidSlack("other", ts, sig, "payload=x", Now));
        Assert.False(InboundAckSignatures.IsValidSlack(Secret, ts, sig, "payload=y", Now));
        Assert.False(InboundAckSignatures.IsValidSlack(Secret, ts, null, "payload=x", Now));
        Assert.False(InboundAckSignatures.IsValidSlack("", ts, sig, "payload=x", Now));
        Assert.False(InboundAckSignatures.IsValidSlack(Secret, "not-a-number", sig, "payload=x", Now));
    }

    [Fact]
    public void Slack_rejects_a_replayed_request_outside_the_tolerance()
    {
        var old = Now.AddMinutes(-6).ToUnixTimeSeconds().ToString();
        Assert.False(InboundAckSignatures.IsValidSlack(Secret, old, SlackSig(old, "b"), "b", Now));
        var recent = Now.AddMinutes(-4).ToUnixTimeSeconds().ToString();
        Assert.True(InboundAckSignatures.IsValidSlack(Secret, recent, SlackSig(recent, "b"), "b", Now));
    }

    [Fact]
    public void PagerDuty_accepts_any_matching_signature_in_the_header()
    {
        var body = Encoding.UTF8.GetBytes("{\"event\":{}}");
        var good = $"v1={Hex("{\"event\":{}}")}";
        Assert.True(InboundAckSignatures.IsValidPagerDuty(Secret, good, body));
        Assert.True(InboundAckSignatures.IsValidPagerDuty(Secret, $"v1=deadbeef, {good}", body));
    }

    [Fact]
    public void PagerDuty_rejects_a_wrong_secret_tampered_body_or_missing_header()
    {
        var body = Encoding.UTF8.GetBytes("{\"event\":{}}");
        var good = $"v1={Hex("{\"event\":{}}")}";
        Assert.False(InboundAckSignatures.IsValidPagerDuty("other", good, body));
        Assert.False(InboundAckSignatures.IsValidPagerDuty(Secret, good, Encoding.UTF8.GetBytes("{}")));
        Assert.False(InboundAckSignatures.IsValidPagerDuty(Secret, null, body));
        Assert.False(InboundAckSignatures.IsValidPagerDuty("", good, body));
    }

    [Fact]
    public void Dedup_key_round_trips_to_the_rule_id_and_other_keys_are_ignored()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, PagerDutyAlertNotifier.RuleIdFromDedupKey($"flare-alert-{id:N}"));
        Assert.Null(PagerDutyAlertNotifier.RuleIdFromDedupKey($"flare-test-{id:N}"));
        Assert.Null(PagerDutyAlertNotifier.RuleIdFromDedupKey("flare-alert-nope"));
        Assert.Null(PagerDutyAlertNotifier.RuleIdFromDedupKey(null));
    }

    [Fact]
    public void Slack_blocks_need_a_real_firing_slack_send_with_a_secret_and_a_link()
    {
        const string hook = "https://hooks.slack.com/services/T/B/x";
        const string link = "https://flare.example.com/ack?token=abc%2Bdef";

        var blocks = WebhookAlertNotifier.SlackAckBlocks(hook, "text", link, false, false, "secret");
        Assert.NotNull(blocks);
        var value = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(blocks)).RootElement[1].GetProperty("elements")[0].GetProperty("value").GetString();
        Assert.Equal("abc+def", value);

        Assert.Null(WebhookAlertNotifier.SlackAckBlocks(hook, "text", link, false, false, ""));
        Assert.Null(WebhookAlertNotifier.SlackAckBlocks(hook, "text", link, true, false, "secret"));
        Assert.Null(WebhookAlertNotifier.SlackAckBlocks(hook, "text", link, false, true, "secret"));
        Assert.Null(WebhookAlertNotifier.SlackAckBlocks(hook, "text", null, false, false, "secret"));
        Assert.Null(WebhookAlertNotifier.SlackAckBlocks("https://example.com/hook", "text", link, false, false, "secret"));
    }
}
