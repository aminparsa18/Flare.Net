using System.Net;
using System.Text.Json;
using Flare.Api.Alerting;
using Flare.Api.Model;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>Covers the create/close requests <see cref="JsmOpsAlertNotifier"/> sends, via a capturing in-process handler.</summary>
public class JsmOpsAlertNotifierTests
{
    private static readonly AlertRule Rule = new()
    {
        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Name = "High error rate",
        Condition = new LogFilter(),
        Threshold = new AlertThreshold { Count = 10 },
        WindowSeconds = 60,
        Severity = AlertSeverity.Critical,
        Labels = new Dictionary<string, string> { ["team"] = "payments" },
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private static readonly NotificationChannel Channel = new()
    {
        Id = Guid.NewGuid(),
        Name = "jsm",
        Type = NotificationChannelType.JsmOps,
        JsmOpsApiKey = "key123",
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private sealed class CapturingHandler(HttpStatusCode status, string responseBody = "") : HttpMessageHandler
    {
        public string? Url { get; private set; }

        public string? Body { get; private set; }

        public string? Auth { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Auth = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody) };
        }
    }

    private static async Task<(NotificationResult Result, CapturingHandler Handler)> SendAsync(bool resolved = false, bool isTest = false, HttpStatusCode status = HttpStatusCode.Accepted, string responseBody = "")
    {
        var handler = new CapturingHandler(status, responseBody);
        var result = await new JsmOpsAlertNotifier(new HttpClient(handler), Options.Create(new AlertLinkOptions()))
            .SendAsync(Rule, Channel, 42, DateTimeOffset.UnixEpoch, CancellationToken.None, isTest: isTest, resolved: resolved);
        return (result, handler);
    }

    [Fact]
    public async Task Fire_CreatesAnAliasedAlertWithGenieKeyAuth()
    {
        var (result, handler) = await SendAsync();

        Assert.True(result.Success);
        Assert.Equal("https://api.atlassian.com/jsm/ops/integration/v2/alerts", handler.Url);
        Assert.Equal("GenieKey key123", handler.Auth);
        var body = JsonDocument.Parse(handler.Body!).RootElement;
        Assert.Equal($"flare-alert-{Rule.Id:N}", body.GetProperty("alias").GetString());
        Assert.Equal("P1", body.GetProperty("priority").GetString());
        Assert.Contains("team:payments", body.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
    }

    [Fact]
    public async Task Recovery_ClosesTheAlertByAlias()
    {
        var (result, handler) = await SendAsync(resolved: true);

        Assert.True(result.Success);
        Assert.Equal($"https://api.atlassian.com/jsm/ops/integration/v2/alerts/flare-alert-{Rule.Id:N}/close?identifierType=alias", handler.Url);
    }

    [Fact]
    public async Task Test_UsesAOneOffAliasAndNeverCloses()
    {
        var (_, handler) = await SendAsync(isTest: true, resolved: true);

        Assert.EndsWith("/v2/alerts", handler.Url);
        Assert.StartsWith("flare-test-", JsonDocument.Parse(handler.Body!).RootElement.GetProperty("alias").GetString());
    }

    [Fact]
    public async Task Rejection_SurfacesTheResponseBody()
    {
        var (result, _) = await SendAsync(status: HttpStatusCode.Unauthorized, responseBody: "invalid key");

        Assert.False(result.Success);
        Assert.Contains("invalid key", result.Error);
    }

    [Theory]
    [InlineData(AlertSeverity.Critical, "P1")]
    [InlineData(AlertSeverity.Error, "P2")]
    [InlineData(AlertSeverity.Warning, "P3")]
    [InlineData(AlertSeverity.Info, "P5")]
    public void Priority_MapsSeverity(AlertSeverity severity, string expected)
    {
        Assert.Equal(expected, JsmOpsAlertNotifier.Priority(severity));
    }

    [Fact]
    public void Truncate_CapsToTheLimit()
    {
        Assert.Equal(130, JsmOpsAlertNotifier.Truncate(new string('x', 500), JsmOpsAlertNotifier.MaxMessageLength).Length);
    }
}
