using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flare.Api.Alerting;
using Flare.Api.Endpoints;
using Flare.Api.Model;
using Flare.Api.Query;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>Covers the Slack button and PagerDuty sync handlers (ADR-0138) over a stub alert store: which requests are refused, and which write an ack.</summary>
public class AlertAckIntegrationEndpointsTests
{
    private const string SlackSecret = "slack-secret";
    private const string PdSecret = "pd-secret";
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_760_000_000);

    public class Store : DispatchProxy
    {
        public AlertRule? Rule { get; set; }

        public AlertFiringState? State { get; set; }

        public List<AlertAck> Acks { get; } = [];

        protected override object? Invoke(MethodInfo? method, object?[]? args) => method!.Name switch
        {
            "GetAsync" => Task.FromResult(Rule),
            "GetFiringStatesAsync" => Task.FromResult<IReadOnlyDictionary<Guid, AlertFiringState>>(State is null || Rule is null ? new Dictionary<Guid, AlertFiringState>() : new Dictionary<Guid, AlertFiringState> { [Rule.Id] = State }),
            "InsertAckAsync" => Record((AlertAck)args![0]!),
            _ => throw new NotSupportedException(method.Name),
        };

        private Task Record(AlertAck ack)
        {
            Acks.Add(ack);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static (IAlertQueryService Alerts, Store Store) NewStore(bool firing = true, AlertAck? ack = null)
    {
        var alerts = DispatchProxy.Create<IAlertQueryService, Store>();
        var store = (Store)(object)alerts;
        var now = DateTimeOffset.UtcNow;
        store.Rule = new AlertRule { Id = Guid.NewGuid(), Name = "checkout errors", Condition = new LogFilter(), Threshold = new AlertThreshold { Count = 1 }, WindowSeconds = 300, CreatedAt = now, UpdatedAt = now };
        store.State = firing ? new AlertFiringState(now, true) { Ack = ack } : null;
        return (alerts, store);
    }

    private static IOptions<AlertLinkOptions> Opts() => Options.Create(new AlertLinkOptions { PublicUrl = "https://flare.example.com", SlackSigningSecret = SlackSecret, PagerDutyWebhookSecret = PdSecret });

    private static string Hex(string secret, byte[] data) => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), data));

    private static Task<int> StatusAsync(IResult result) => Task.FromResult(Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode ?? 200);

    private static HttpContext Request(byte[] body, params (string, string)[] headers)
    {
        var ctx = new DefaultHttpContext();
        ctx.Request.Body = new MemoryStream(body);
        ctx.Request.ContentLength = body.Length;
        foreach (var (k, v) in headers)
        {
            ctx.Request.Headers[k] = v;
        }

        return ctx;
    }

    private sealed class NoClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new HttpClientHandler()) { BaseAddress = null };
    }

    private static async Task<(int Status, Store Store)> SlackAsync(Func<Store, string> tokenFor, bool sign = true, bool firing = true, AlertAck? ack = null, string? secret = SlackSecret)
    {
        var (alerts, store) = NewStore(firing, ack);
        var signer = new AlertAckLinkSigner(new EphemeralDataProtectionProvider(), Options.Create(new AlertLinkOptions { PublicUrl = "https://flare.example.com" }));
        var token = AlertAckLinkSigner.TokenFromUrl(signer.CreateUrl(store.Rule!.Id, Now.AddMinutes(-1)));
        var payload = JsonSerializer.Serialize(new { type = "block_actions", user = new { name = "dana" }, actions = new[] { new { action_id = "flare_ack", value = token } } });
        var body = Encoding.UTF8.GetBytes("payload=" + Uri.EscapeDataString(payload));
        var ts = Now.ToUnixTimeSeconds().ToString();
        var sig = sign ? "v0=" + Hex(SlackSecret, Encoding.UTF8.GetBytes($"v0:{ts}:{Encoding.UTF8.GetString(body)}")) : "v0=bad";
        var options = Options.Create(new AlertLinkOptions { SlackSigningSecret = secret ?? "" });
        var result = await AlertAckIntegrationEndpoints.HandleSlackAsync(Request(body, ("X-Slack-Request-Timestamp", ts), ("X-Slack-Signature", sig)), options, signer, alerts, new NoClients(), new FixedTime(Now), default);
        return (await StatusAsync(result), store);
    }

    [Fact]
    public async Task Slack_valid_click_on_a_firing_rule_records_an_ack_by_the_slack_user()
    {
        var (status, store) = await SlackAsync(_ => "");
        Assert.Equal(200, status);
        var ack = Assert.Single(store.Acks);
        Assert.Equal(AlertAckKind.Ack, ack.Kind);
        Assert.Equal("Slack: dana", ack.AckedBy);
    }

    [Fact]
    public async Task Slack_bad_signature_is_401_and_writes_nothing()
    {
        var (status, store) = await SlackAsync(_ => "", sign: false);
        Assert.Equal(401, status);
        Assert.Empty(store.Acks);
    }

    [Fact]
    public async Task Slack_is_404_without_a_secret()
    {
        var (status, store) = await SlackAsync(_ => "", secret: null);
        Assert.Equal(404, status);
        Assert.Empty(store.Acks);
    }

    [Fact]
    public async Task Slack_click_on_a_resolved_or_already_acked_incident_writes_nothing()
    {
        Assert.Empty((await SlackAsync(_ => "", firing: false)).Store.Acks);
        var acked = new AlertAck(Guid.NewGuid(), Now, "someone", AlertAckKind.Ack, null, "");
        Assert.Empty((await SlackAsync(_ => "", ack: acked)).Store.Acks);
    }

    private static async Task<(int Status, Store Store)> PagerDutyAsync(string eventType, string incidentKey, AlertAck? ack = null, bool sign = true, bool firing = true, string? secret = PdSecret)
    {
        var (alerts, store) = NewStore(firing, ack);
        var key = incidentKey.Replace("{id}", store.Rule!.Id.ToString("N"));
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { @event = new { event_type = eventType, data = new { incident_key = key }, agent = new { summary = "Priya" } } }));
        var sig = sign ? "v1=" + Hex(PdSecret, body) : "v1=bad";
        var result = await AlertAckIntegrationEndpoints.HandlePagerDutyAsync(Request(body, ("X-PagerDuty-Signature", sig)), Options.Create(new AlertLinkOptions { PagerDutyWebhookSecret = secret ?? "" }), alerts, new FixedTime(Now), default);
        return (await StatusAsync(result), store);
    }

    [Fact]
    public async Task PagerDuty_acknowledged_records_an_ack_for_the_matching_rule()
    {
        var (status, store) = await PagerDutyAsync("incident.acknowledged", "flare-alert-{id}");
        Assert.Equal(200, status);
        var ack = Assert.Single(store.Acks);
        Assert.Equal(AlertAckKind.Ack, ack.Kind);
        Assert.Equal("PagerDuty: Priya", ack.AckedBy);
    }

    [Fact]
    public async Task PagerDuty_ignores_foreign_keys_other_events_and_a_non_firing_rule_but_still_answers_200()
    {
        Assert.Empty((await PagerDutyAsync("incident.acknowledged", "someone-elses-key")).Store.Acks);
        Assert.Empty((await PagerDutyAsync("incident.resolved", "flare-alert-{id}")).Store.Acks);
        var (status, store) = await PagerDutyAsync("incident.acknowledged", "flare-alert-{id}", firing: false);
        Assert.Equal(200, status);
        Assert.Empty(store.Acks);
    }

    [Fact]
    public async Task PagerDuty_unacknowledged_clears_only_an_ack_that_came_from_pagerduty()
    {
        var fromPd = new AlertAck(Guid.NewGuid(), Now, "PagerDuty: Priya", AlertAckKind.Ack, null, "");
        var cleared = Assert.Single((await PagerDutyAsync("incident.unacknowledged", "flare-alert-{id}", fromPd)).Store.Acks);
        Assert.Equal(AlertAckKind.Clear, cleared.Kind);

        var fromFlare = new AlertAck(Guid.NewGuid(), Now, "dana", AlertAckKind.Ack, null, "");
        Assert.Empty((await PagerDutyAsync("incident.unacknowledged", "flare-alert-{id}", fromFlare)).Store.Acks);
    }

    [Fact]
    public async Task PagerDuty_bad_signature_is_401_and_missing_secret_is_404()
    {
        var bad = await PagerDutyAsync("incident.acknowledged", "flare-alert-{id}", sign: false);
        Assert.Equal(401, bad.Status);
        Assert.Empty(bad.Store.Acks);
        Assert.Equal(404, (await PagerDutyAsync("incident.acknowledged", "flare-alert-{id}", secret: null)).Status);
    }
}
