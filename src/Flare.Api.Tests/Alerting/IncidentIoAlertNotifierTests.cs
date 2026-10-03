using System.Net;
using System.Text.Json;
using Flare.Api.Alerting;
using Flare.Api.Model;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>Covers the Alert Events V2 payload and auth <see cref="IncidentIoAlertNotifier"/> sends, via a capturing in-process handler.</summary>
public class IncidentIoAlertNotifierTests
{
    private static readonly AlertRule Rule = new()
    {
        Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Name = "High error rate",
        Condition = new LogFilter(),
        Threshold = new AlertThreshold { Count = 10 },
        WindowSeconds = 60,
        Labels = new Dictionary<string, string> { ["team"] = "payments" },
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private static readonly NotificationChannel Channel = new()
    {
        Id = Guid.NewGuid(),
        Name = "inc",
        Type = NotificationChannelType.IncidentIo,
        WebhookUrl = "https://api.incident.io/v2/alert_events/http/abc",
        IncidentIoToken = "secret",
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private sealed class CapturingHandler(HttpStatusCode status, string responseBody = "") : HttpMessageHandler
    {
        public string? Body { get; private set; }

        public string? Auth { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Auth = request.Headers.Authorization?.ToString();
            return new HttpResponseMessage(status) { Content = new StringContent(responseBody) };
        }
    }

    private static async Task<(NotificationResult Result, JsonElement Body, string? Auth)> SendAsync(bool resolved = false, bool isTest = false, HttpStatusCode status = HttpStatusCode.Accepted, string responseBody = "")
    {
        var handler = new CapturingHandler(status, responseBody);
        var result = await new IncidentIoAlertNotifier(new HttpClient(handler), Options.Create(new AlertLinkOptions()))
            .SendAsync(Rule, Channel, 42, DateTimeOffset.UnixEpoch, CancellationToken.None, isTest: isTest, resolved: resolved);
        return (result, JsonDocument.Parse(handler.Body!).RootElement, handler.Auth);
    }

    [Fact]
    public async Task Fire_SendsAFiringEventWithBearerAuthAndRuleKey()
    {
        var (result, body, auth) = await SendAsync();

        Assert.True(result.Success);
        Assert.Equal("Bearer secret", auth);
        Assert.Equal("firing", body.GetProperty("status").GetString());
        Assert.Equal($"flare-alert-{Rule.Id:N}", body.GetProperty("deduplication_key").GetString());
        Assert.Equal("payments", body.GetProperty("metadata").GetProperty("team").GetString());
        Assert.Equal("High error rate", body.GetProperty("metadata").GetProperty("rule").GetString());
    }

    [Fact]
    public async Task Recovery_SendsAResolvedEventWithTheSameKey()
    {
        var (_, fired, _) = await SendAsync();
        var (_, resolved, _) = await SendAsync(resolved: true);

        Assert.Equal("resolved", resolved.GetProperty("status").GetString());
        Assert.Equal(fired.GetProperty("deduplication_key").GetString(), resolved.GetProperty("deduplication_key").GetString());
    }

    [Fact]
    public async Task Test_UsesAOneOffKeyAndStaysFiring()
    {
        var (_, body, _) = await SendAsync(isTest: true, resolved: true);

        Assert.StartsWith("flare-test-", body.GetProperty("deduplication_key").GetString());
        Assert.Equal("firing", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Rejection_SurfacesTheResponseBody()
    {
        var (result, _, _) = await SendAsync(status: HttpStatusCode.Unauthorized, responseBody: "invalid token");

        Assert.False(result.Success);
        Assert.Contains("invalid token", result.Error);
    }

    [Fact]
    public void Description_IsCappedUnderTheDocumentedLimit()
    {
        Assert.Equal(IncidentIoAlertNotifier.MaxDescriptionLength, IncidentIoAlertNotifier.CapDescription(new string('x', 500_000)).Length);
    }
}
