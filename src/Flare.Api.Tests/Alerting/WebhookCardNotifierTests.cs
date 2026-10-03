using System.Net;
using System.Text.Json;
using Flare.Api.Alerting;
using Flare.Api.Model;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>
/// Covers the payload shapes <see cref="TeamsAlertNotifier"/> (Adaptive Card envelope) and
/// <see cref="DiscordAlertNotifier"/> (<c>content</c> + mention suppression) build, via an
/// in-process handler that captures the request instead of reaching either service.
/// </summary>
public class WebhookCardNotifierTests
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

    private static NotificationChannel Channel(NotificationChannelType type) => new()
    {
        Id = Guid.NewGuid(),
        Name = "chat",
        Type = type,
        WebhookUrl = "https://example.com/hook",
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private static async Task<(NotificationResult Result, JsonElement Body)> SendAsync(Func<HttpClient, IAlertNotifier> make, NotificationChannelType type, AlertRule? rule = null, HttpStatusCode status = HttpStatusCode.OK)
    {
        var handler = new CapturingHandler(status);
        var result = await make(new HttpClient(handler)).SendAsync(rule ?? Rule, Channel(type), 42, DateTimeOffset.UnixEpoch, CancellationToken.None);
        return (result, JsonDocument.Parse(handler.Body!).RootElement);
    }

    private static IAlertNotifier Teams(HttpClient c, string? publicUrl = null) => new TeamsAlertNotifier(c, Options.Create(new AlertLinkOptions { PublicUrl = publicUrl }));

    private static IAlertNotifier Discord(HttpClient c) => new DiscordAlertNotifier(c, Options.Create(new AlertLinkOptions()));

    [Fact]
    public async Task Teams_SendsAnAdaptiveCardMessageEnvelope()
    {
        var (result, body) = await SendAsync(c => Teams(c), NotificationChannelType.Teams, status: HttpStatusCode.Accepted);

        Assert.True(result.Success);
        Assert.Equal("message", body.GetProperty("type").GetString());
        var attachment = body.GetProperty("attachments")[0];
        Assert.Equal("application/vnd.microsoft.card.adaptive", attachment.GetProperty("contentType").GetString());
        var card = attachment.GetProperty("content");
        Assert.Equal("AdaptiveCard", card.GetProperty("type").GetString());
        Assert.Equal("TextBlock", card.GetProperty("body")[0].GetProperty("type").GetString());
        Assert.False(card.TryGetProperty("actions", out _), "no PublicUrl, so no link buttons");
    }

    [Fact]
    public async Task Teams_WithPublicUrl_AddsOpenUrlButton()
    {
        var (_, body) = await SendAsync(c => Teams(c, "https://flare.example.com"), NotificationChannelType.Teams);

        var action = body.GetProperty("attachments")[0].GetProperty("content").GetProperty("actions")[0];
        Assert.Equal("Action.OpenUrl", action.GetProperty("type").GetString());
        Assert.StartsWith("https://flare.example.com", action.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Teams_CustomTitle_IsABoldFirstBlock()
    {
        var (_, body) = await SendAsync(c => Teams(c), NotificationChannelType.Teams, Rule with { NotificationTitleTemplate = "Errors in {{rule_name}}" });

        var first = body.GetProperty("attachments")[0].GetProperty("content").GetProperty("body")[0];
        Assert.Equal("Bolder", first.GetProperty("weight").GetString());
        Assert.Contains("High error rate", first.GetProperty("text").GetString());
    }

    [Fact]
    public async Task Discord_SendsContentAndSuppressesMentions()
    {
        var (result, body) = await SendAsync(Discord, NotificationChannelType.Discord, status: HttpStatusCode.NoContent);

        Assert.True(result.Success);
        Assert.Contains("High error rate", body.GetProperty("content").GetString());
        Assert.Equal(0, body.GetProperty("allowed_mentions").GetProperty("parse").GetArrayLength());
    }

    [Fact]
    public void Discord_Content_IsTruncatedToTheApiLimit()
    {
        var content = DiscordAlertNotifier.BuildContent(new AlertMessage(null, new string('x', 5000), IsCustom: true));

        Assert.Equal(DiscordAlertNotifier.MaxContentLength, content.Length);
        Assert.EndsWith("…", content);
    }

    [Fact]
    public async Task RejectedPost_IsAFailedResultWithTheServiceMessage()
    {
        var (result, _) = await SendAsync(Discord, NotificationChannelType.Discord, status: HttpStatusCode.NotFound);

        Assert.False(result.Success);
        Assert.Equal(404, result.StatusCode);
        Assert.StartsWith("HTTP 404", result.Error);
    }

    private sealed class CapturingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }
}
