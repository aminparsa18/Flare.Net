using System.Net;
using System.Text.Json;
using Xunit;

namespace Flare.Maui.Tests;

public class FeedbackTests
{
    private sealed class RecordingHandler(HttpStatusCode status = HttpStatusCode.NoContent, bool throws = false) : HttpMessageHandler
    {
        public List<(Uri Uri, string? Auth, string Body)> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (throws) throw new HttpRequestException("offline");
            Requests.Add((request.RequestUri!, request.Headers.Authorization?.ToString(), await request.Content!.ReadAsStringAsync(ct)));
            return new HttpResponseMessage(status);
        }
    }

    private static FlareMauiOptions Options() => new()
    {
        Endpoint = new Uri("http://h:4318/"), ServiceName = "shop", IngestKey = "k1",
    };

    private static FeedbackSender Sender(RecordingHandler handler, string? user = "u1", string? version = "1.2.3") =>
        new(Options(), "abcdef0123456789", new HttpClient(handler), () => user, () => version);

    [Fact]
    public async Task Posts_the_message_with_session_user_and_version()
    {
        var handler = new RecordingHandler();

        Assert.True(await Sender(handler).SendAsync("  It froze  ", "a@b.c", "Ann", default));

        var (uri, auth, body) = Assert.Single(handler.Requests);
        Assert.Equal("http://h:4318/v1/feedback", uri.ToString());
        Assert.Equal("Bearer k1", auth);
        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        Assert.Equal("shop", root.GetProperty("service").GetString());
        Assert.Equal("abcdef0123456789", root.GetProperty("session_id").GetString());
        Assert.Equal("It froze", root.GetProperty("message").GetString());
        Assert.Equal("u1", root.GetProperty("user_id").GetString());
        Assert.Equal("a@b.c", root.GetProperty("email").GetString());
        Assert.Equal("Ann", root.GetProperty("name").GetString());
        Assert.Equal("1.2.3", root.GetProperty("app_version").GetString());
    }

    [Fact]
    public async Task Omits_unset_optional_fields()
    {
        var handler = new RecordingHandler();

        await Sender(handler, user: null, version: null).SendAsync("hi", null, "  ", default);

        using var json = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal(["service", "session_id", "message"], json.RootElement.EnumerateObject().Select(p => p.Name));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Empty_message_is_not_sent(string message)
    {
        var handler = new RecordingHandler();
        Assert.False(await Sender(handler).SendAsync(message, null, null, default));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Long_message_is_cut_before_sending()
    {
        var handler = new RecordingHandler();
        await Sender(handler).SendAsync(new string('m', FeedbackSender.MaxMessageChars + 10), null, null, default);
        using var json = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal(FeedbackSender.MaxMessageChars, json.RootElement.GetProperty("message").GetString()!.Length);
    }

    [Fact]
    public async Task Rejection_and_network_failure_return_false_without_throwing()
    {
        Assert.False(await Sender(new RecordingHandler(HttpStatusCode.ServiceUnavailable)).SendAsync("hi", null, null, default));
        Assert.False(await Sender(new RecordingHandler(throws: true)).SendAsync("hi", null, null, default));
    }

    [Fact]
    public async Task Public_api_returns_false_before_initialization() =>
        Assert.False(await FlareMaui.SendFeedbackAsync("hi"));

    [Fact]
    public void Enrichment_exposes_the_current_user_id()
    {
        var e = new FlareEnrichment(false);
        Assert.Null(e.Get("user.id"));
        e.SetUser("u1", null, null);
        Assert.Equal("u1", e.Get("user.id"));
    }
}
