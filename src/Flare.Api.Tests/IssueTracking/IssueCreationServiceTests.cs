using System.Net;
using Flare.Api.IssueTracking;
using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Identity.IssueTrackers;
using Flare.Identity.SourceLinks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Flare.Api.Tests.IssueTracking;

/// <summary>The service end to end against a canned host: HTTP and the group store faked, everything else real.</summary>
public class IssueCreationServiceTests
{
    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeLinks(params SourceLinkConfig[] configs) : ISourceLinkStore
    {
        public Task<IReadOnlyList<SourceLinkConfig>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SourceLinkConfig>>(configs);

        public Task SetAsync(SourceLinkConfig config, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(string serviceName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeTrackers(params IssueTrackerConfig[] configs) : IIssueTrackerStore
    {
        public Task<IReadOnlyList<IssueTrackerConfig>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<IssueTrackerConfig>>(configs);

        public Task SetAsync(IssueTrackerConfig config, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(string serviceName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeIssues(string existingUrl = "", bool failUpsert = false) : IErrorIssueQueryService
    {
        public List<ErrorIssueRequest> Upserts { get; } = [];

        public Task<string> GetIssueUrlAsync(string exceptionType, string exceptionMessage, CancellationToken cancellationToken) => Task.FromResult(existingUrl);

        public Task<ErrorIssue?> UpsertAsync(ErrorIssueRequest request, string changedBy, CancellationToken cancellationToken)
        {
            if (failUpsert)
            {
                throw new InvalidOperationException("clickhouse down");
            }

            Upserts.Add(request);
            return Task.FromResult<ErrorIssue?>(new ErrorIssue
            {
                Id = "id", ExceptionType = request.ExceptionType, ExceptionMessage = request.ExceptionMessage, Status = ErrorIssueStatus.Open,
                StatusChangedAt = DateTimeOffset.UnixEpoch, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch, IssueUrl = request.IssueUrl ?? ""
            });
        }

        public Task<IReadOnlyList<ErrorIssue>> ListAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListIgnoredKeysAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, string>> ListAssigneesAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task RekeyAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CannedHandler(HttpStatusCode status, string body, TaskCompletionSource? gate = null) : HttpMessageHandler
    {
        public int Calls;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref Calls);
            if (gate is not null)
            {
                await gate.Task;
            }

            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private static readonly SourceLinkConfig GitHub = new("shop", SourceLinkProvider.GitHub, "https://github.com/acme/shop", "main", "", null, "w-t0k");

    private static CreateTrackerIssueRequest Request(IssueTrackerTarget target = IssueTrackerTarget.GitHub) =>
        new() { Target = target, ServiceName = "shop", ExceptionType = "System.InvalidOperationException", ExceptionMessage = "boom", Title = "T", Body = "B" };

    private static IssueCreationService Service(CannedHandler handler, FakeIssues issues, SourceLinkConfig[]? links = null, IssueTrackerConfig[]? trackers = null) =>
        new(new FakeFactory(handler), new FakeLinks(links ?? [GitHub]), new FakeTrackers(trackers ?? []), issues, NullLogger<IssueCreationService>.Instance);

    [Fact]
    public async Task Creates_ThenLinksTheNewUrlToTheGroup()
    {
        var handler = new CannedHandler(HttpStatusCode.Created, """{"html_url":"https://github.com/acme/shop/issues/9"}""");
        var issues = new FakeIssues();

        var outcome = await Service(handler, issues).CreateAsync(Request(), "amin", default);

        Assert.Equal(200, outcome.StatusCode);
        Assert.Equal("https://github.com/acme/shop/issues/9", outcome.Issue!.IssueUrl);
        Assert.Equal("https://github.com/acme/shop/issues/9", Assert.Single(issues.Upserts).IssueUrl);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task AnOmittedBodyAndMessage_AreTreatedAsEmpty()
    {
        var handler = new CannedHandler(HttpStatusCode.Created, """{"html_url":"https://github.com/acme/shop/issues/9"}""");
        var issues = new FakeIssues();

        var outcome = await Service(handler, issues).CreateAsync(Request() with { ExceptionMessage = null!, Body = null! }, "amin", default);

        Assert.Equal(200, outcome.StatusCode);
        Assert.Equal("", Assert.Single(issues.Upserts).ExceptionMessage);
    }

    [Fact]
    public async Task AGroupThatAlreadyHasALink_IsRefused_WithoutCallingTheHost()
    {
        var handler = new CannedHandler(HttpStatusCode.Created, "{}");

        var outcome = await Service(handler, new FakeIssues(existingUrl: "https://github.com/acme/shop/issues/1")).CreateAsync(Request(), "amin", default);

        Assert.Equal(409, outcome.StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ADoubleClick_CreatesOnlyOneIssue()
    {
        var gate = new TaskCompletionSource();
        var handler = new CannedHandler(HttpStatusCode.Created, """{"html_url":"https://github.com/acme/shop/issues/9"}""", gate);
        var service = Service(handler, new FakeIssues());

        var first = service.CreateAsync(Request(), "amin", default);
        while (handler.Calls == 0)
        {
            await Task.Yield();
        }

        var second = await service.CreateAsync(Request(), "amin", default);
        gate.SetResult();

        Assert.Equal(409, second.StatusCode);
        Assert.Equal(200, (await first).StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task ADifferentGroup_IsNotBlockedByAnInFlightCreate()
    {
        var gate = new TaskCompletionSource();
        var handler = new CannedHandler(HttpStatusCode.Created, """{"html_url":"https://github.com/acme/shop/issues/9"}""", gate);
        var service = Service(handler, new FakeIssues());

        var first = service.CreateAsync(Request(), "amin", default);
        var second = service.CreateAsync(Request() with { ExceptionMessage = "other" }, "amin", default);
        while (handler.Calls < 2)
        {
            await Task.Yield();
        }

        gate.SetResult();
        Assert.Equal(200, (await first).StatusCode);
        Assert.Equal(200, (await second).StatusCode);
    }

    [Fact]
    public async Task AFailedCreate_ReleasesTheGroup_SoARetryIsAllowed()
    {
        var service = Service(new CannedHandler(HttpStatusCode.Unauthorized, """{"message":"Bad credentials"}"""), new FakeIssues());

        var first = await service.CreateAsync(Request(), "amin", default);
        var second = await service.CreateAsync(Request(), "amin", default);

        Assert.Equal(502, first.StatusCode);
        Assert.Contains("401", first.Error);
        Assert.Contains("Bad credentials", first.Error);
        Assert.Equal(502, second.StatusCode);
    }

    [Fact]
    public async Task WithoutAnIssueToken_FailsBeforeCallingTheHost_EvenWhenAReadTokenExists()
    {
        var handler = new CannedHandler(HttpStatusCode.Created, "{}");
        var readOnly = GitHub with { AccessToken = "r-t0k", IssueToken = null };

        var outcome = await Service(handler, new FakeIssues(), links: [readOnly]).CreateAsync(Request(), "amin", default);

        Assert.Equal(422, outcome.StatusCode);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task AProviderMismatch_IsRefused()
    {
        var outcome = await Service(new CannedHandler(HttpStatusCode.Created, "{}"), new FakeIssues()).CreateAsync(Request(IssueTrackerTarget.GitLab), "amin", default);

        Assert.Equal(422, outcome.StatusCode);
    }

    [Fact]
    public async Task Jira_NeedsATokenAndAnIssueType()
    {
        var handler = new CannedHandler(HttpStatusCode.Created, """{"key":"OPS-1"}""");
        var noType = new IssueTrackerConfig("shop", IssueTrackerProvider.Jira, "https://acme.atlassian.net", "10000", "", "", "tok");
        var complete = noType with { IssueTypeId = "10001" };

        Assert.Equal(422, (await Service(handler, new FakeIssues(), trackers: [noType]).CreateAsync(Request(IssueTrackerTarget.Jira), "amin", default)).StatusCode);

        var ok = await Service(handler, new FakeIssues(), trackers: [complete]).CreateAsync(Request(IssueTrackerTarget.Jira), "amin", default);
        Assert.Equal("https://acme.atlassian.net/browse/OPS-1", ok.Issue!.IssueUrl);
    }

    [Fact]
    public async Task WhenLinkingFails_TheMessageNamesTheCreatedIssue()
    {
        var handler = new CannedHandler(HttpStatusCode.Created, """{"html_url":"https://github.com/acme/shop/issues/9"}""");

        var outcome = await Service(handler, new FakeIssues(failUpsert: true)).CreateAsync(Request(), "amin", default);

        Assert.Equal(500, outcome.StatusCode);
        Assert.Contains("https://github.com/acme/shop/issues/9", outcome.Error);
    }
}
