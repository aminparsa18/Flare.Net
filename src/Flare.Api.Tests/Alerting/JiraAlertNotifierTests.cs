using System.Net;
using System.Text;
using System.Text.Json;
using Flare.Api.Alerting;
using Flare.Api.Model;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>
/// Covers <see cref="JiraAlertNotifier"/>'s request sequence (search, create/comment,
/// transition) against a scripted in-process handler instead of Jira Cloud.
/// </summary>
public class JiraAlertNotifierTests
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
        Name = "jira",
        Type = NotificationChannelType.Jira,
        JiraBaseUrl = "https://acme.atlassian.net",
        JiraEmail = "bot@acme.com",
        JiraApiToken = "tok",
        JiraProjectKey = "OPS",
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private sealed class ScriptedHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        private int _next;

        public List<(HttpMethod Method, string Path, string? Body, string? Auth)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.Method, request.RequestUri!.PathAndQuery, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken), request.Headers.Authorization?.ToString()));
            var (status, body) = responses[_next++];
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static async Task<NotificationResult> SendAsync(ScriptedHandler handler, bool resolved = false, bool isTest = false) =>
        await new JiraAlertNotifier(new HttpClient(handler), Options.Create(new AlertLinkOptions()))
            .SendAsync(Rule, Channel, 42, DateTimeOffset.UnixEpoch, CancellationToken.None, isTest: isTest, resolved: resolved);

    private const string NoIssues = """{"issues":[]}""";
    private const string OneIssue = """{"issues":[{"key":"OPS-7"}]}""";

    [Fact]
    public async Task Fire_WithNoOpenIssue_CreatesOneLabelledByTheRule()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, NoIssues), (HttpStatusCode.Created, """{"key":"OPS-8"}"""));

        var result = await SendAsync(handler);

        Assert.True(result.Success);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains($"labels = \"flare-alert-{Rule.Id:N}\"", JsonDocument.Parse(handler.Requests[0].Body!).RootElement.GetProperty("jql").GetString());
        Assert.Equal("Basic Ym90QGFjbWUuY29tOnRvaw==", handler.Requests[0].Auth);
        Assert.Equal("/rest/api/3/issue", handler.Requests[1].Path);
        var fields = JsonDocument.Parse(handler.Requests[1].Body!).RootElement.GetProperty("fields");
        Assert.Equal("OPS", fields.GetProperty("project").GetProperty("key").GetString());
        Assert.Equal("Task", fields.GetProperty("issuetype").GetProperty("name").GetString());
        Assert.Equal("doc", fields.GetProperty("description").GetProperty("type").GetString());
        Assert.Contains($"flare-alert-{Rule.Id:N}", fields.GetProperty("labels").EnumerateArray().Select(l => l.GetString()));
    }

    [Fact]
    public async Task Fire_WithOpenIssue_AddsACommentInsteadOfCreating()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, OneIssue), (HttpStatusCode.Created, "{}"));

        var result = await SendAsync(handler);

        Assert.True(result.Success);
        Assert.Equal("/rest/api/3/issue/OPS-7/comment", handler.Requests[1].Path);
    }

    [Fact]
    public async Task Resolve_CommentsAndTransitionsToTheDoneCategory()
    {
        var transitions = """{"transitions":[{"id":"11","to":{"statusCategory":{"key":"indeterminate"}}},{"id":"31","to":{"statusCategory":{"key":"done"}}}]}""";
        var handler = new ScriptedHandler((HttpStatusCode.OK, OneIssue), (HttpStatusCode.Created, "{}"), (HttpStatusCode.OK, transitions), (HttpStatusCode.NoContent, ""));

        var result = await SendAsync(handler, resolved: true);

        Assert.True(result.Success);
        Assert.Equal(4, handler.Requests.Count);
        Assert.Equal("/rest/api/3/issue/OPS-7/transitions", handler.Requests[3].Path);
        Assert.Equal("31", JsonDocument.Parse(handler.Requests[3].Body!).RootElement.GetProperty("transition").GetProperty("id").GetString());
    }

    [Fact]
    public async Task Resolve_WithNoOpenIssue_SucceedsWithoutWriting()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, NoIssues));

        var result = await SendAsync(handler, resolved: true);

        Assert.True(result.Success);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Resolve_WithNoDoneTransition_Fails()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, OneIssue), (HttpStatusCode.Created, "{}"), (HttpStatusCode.OK, """{"transitions":[]}"""));

        var result = await SendAsync(handler, resolved: true);

        Assert.False(result.Success);
        Assert.Contains("OPS-7", result.Error);
    }

    [Fact]
    public async Task Test_SkipsTheSearchAndCreatesAFreshIssue()
    {
        var handler = new ScriptedHandler((HttpStatusCode.Created, """{"key":"OPS-9"}"""));

        var result = await SendAsync(handler, isTest: true);

        Assert.True(result.Success);
        Assert.Single(handler.Requests);
        Assert.Equal("/rest/api/3/issue", handler.Requests[0].Path);
    }

    [Fact]
    public async Task Rejection_SurfacesJiraErrorMessages()
    {
        var handler = new ScriptedHandler((HttpStatusCode.OK, NoIssues), (HttpStatusCode.BadRequest, """{"errorMessages":[],"errors":{"issuetype":"Specify a valid issue type"}}"""));

        var result = await SendAsync(handler);

        Assert.False(result.Success);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("Specify a valid issue type", result.Error);
    }

    [Fact]
    public void Summary_IsFirstLineCappedTo255()
    {
        var summary = JiraAlertNotifier.BuildSummary(new AlertMessage(null, new string('x', 400) + "\nsecond", false));

        Assert.Equal(255, summary.Length);
        Assert.DoesNotContain("second", summary);
    }
}
