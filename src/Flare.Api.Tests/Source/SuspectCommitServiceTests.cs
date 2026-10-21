using System.Net;
using Flare.Api.Model;
using Flare.Api.Source;
using Flare.Identity.SourceLinks;
using Xunit;

namespace Flare.Api.Tests.Source;

/// <summary>The service end to end against a canned GitHub: HTTP faked at the handler, everything else real.</summary>
public class SuspectCommitServiceTests
{
    private sealed class FakeFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeStore(params SourceLinkConfig[] configs) : ISourceLinkStore
    {
        public Task<IReadOnlyList<SourceLinkConfig>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SourceLinkConfig>>(configs);

        public Task SetAsync(SourceLinkConfig config, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(string serviceName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CannedHandler(Func<string, (HttpStatusCode, string)> respond) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            lock (Urls)
            {
                Urls.Add(url);
            }

            var (status, body) = respond(url);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private static readonly SourceLinkConfig GitHub = new("shop", SourceLinkProvider.GitHub, "https://github.com/acme/shop", "main", "");

    private static SuspectCommitsRequest Request(params string[] paths) =>
        new() { ServiceName = "shop", BaseRef = "aaa1111", HeadRef = "bbb2222", Paths = paths };

    private static string Compare(string filesJson, params string[] shas)
    {
        var commits = shas.Select((s, i) =>
            "{\"sha\":\"" + s + "\",\"commit\":{\"message\":\"commit " + s + "\",\"author\":{\"name\":\"dev" + i + "\",\"date\":\"2026-10-0" + (i + 1) + "T00:00:00Z\"}}}");
        return "{\"total_commits\":" + shas.Length + ",\"commits\":[" + string.Join(',', commits) + "],\"files\":" + filesJson + "}";
    }

    [Fact]
    public async Task RangeFileList_RulesOutFrames_WithoutInspectingCommits()
    {
        var handler = new CannedHandler(_ => (HttpStatusCode.OK, Compare("""[{"filename":"README.md"}]""", "c1", "c2")));
        var service = new SuspectCommitService(new FakeFactory(handler), new FakeStore(GitHub));

        var (result, error) = await service.GetAsync(Request("src/Cart.cs"), default);

        Assert.Null(error);
        Assert.Empty(result!.Commits);
        Assert.Equal(2, result.TotalCommits);
        Assert.Single(handler.Urls);
        Assert.Equal("https://github.com/acme/shop/compare/aaa1111...bbb2222", result.CompareUrl);
    }

    [Fact]
    public async Task InspectsCommits_AndReturnsTheOnesTouchingTheStack()
    {
        var handler = new CannedHandler(url => url.Contains("/compare/")
            ? (HttpStatusCode.OK, Compare("""[{"filename":"src/Cart.cs"},{"filename":"README.md"}]""", "c1", "c2"))
            : url.EndsWith("/commits/c2")
                ? (HttpStatusCode.OK, """{"files":[{"filename":"src/Cart.cs"}]}""")
                : (HttpStatusCode.OK, """{"files":[{"filename":"README.md"}]}"""));
        var service = new SuspectCommitService(new FakeFactory(handler), new FakeStore(GitHub));

        var (result, error) = await service.GetAsync(Request("src/Cart.cs", "src/Other.cs"), default);

        Assert.Null(error);
        var suspect = Assert.Single(result!.Commits);
        Assert.Equal("c2", suspect.Sha);
        Assert.Equal("https://github.com/acme/shop/commit/c2", suspect.Url);
        Assert.Equal(["src/Cart.cs"], suspect.MatchedPaths);
        Assert.False(result.Truncated);
        Assert.Equal(3, handler.Urls.Count);
    }

    [Fact]
    public async Task SameRequestTwice_IsServedFromCache()
    {
        var handler = new CannedHandler(_ => (HttpStatusCode.OK, Compare("""[{"filename":"README.md"}]""", "c1")));
        var service = new SuspectCommitService(new FakeFactory(handler), new FakeStore(GitHub));

        await service.GetAsync(Request("src/Cart.cs"), default);
        await service.GetAsync(Request("src/Cart.cs"), default);

        Assert.Single(handler.Urls);
    }

    [Fact]
    public async Task HostRejection_IsAUserFacingError()
    {
        var handler = new CannedHandler(_ => (HttpStatusCode.NotFound, "{}"));
        var service = new SuspectCommitService(new FakeFactory(handler), new FakeStore(GitHub));

        var (result, error) = await service.GetAsync(Request("src/Cart.cs"), default);

        Assert.Null(result);
        Assert.Contains("404", error);
    }

    [Theory]
    [InlineData("shop", "aaa", "bbb", "../etc/passwd")]
    [InlineData("shop", "a a", "bbb", "src/A.cs")]
    [InlineData("unknown", "aaa", "bbb", "src/A.cs")]
    public async Task InvalidInput_NeverReachesTheHost(string service, string baseRef, string headRef, string path)
    {
        var handler = new CannedHandler(_ => (HttpStatusCode.OK, "{}"));
        var subject = new SuspectCommitService(new FakeFactory(handler), new FakeStore(GitHub));

        var (result, error) = await subject.GetAsync(new SuspectCommitsRequest { ServiceName = service, BaseRef = baseRef, HeadRef = headRef, Paths = [path] }, default);

        Assert.Null(result);
        Assert.NotNull(error);
        Assert.Empty(handler.Urls);
    }
}
