using Flare.Api.Source;
using Flare.Identity.SourceLinks;
using Xunit;

namespace Flare.Api.Tests.Source;

public class SuspectCommitTests
{
    private static SourceLinkConfig Config(SourceLinkProvider provider, string url, string? token = null) =>
        new("svc", provider, url, "main", "", token);

    [Fact]
    public async Task GitHub_CompareAndCommitRequests()
    {
        var cfg = Config(SourceLinkProvider.GitHub, "https://github.com/acme/shop", "t0k");
        using var compare = SuspectCommitRequestBuilder.BuildCompare(cfg, "aaa1111", "bbb2222")!;
        using var files = SuspectCommitRequestBuilder.BuildCommitFiles(cfg, "bbb2222")!;

        Assert.Equal("https://api.github.com/repos/acme/shop/compare/aaa1111...bbb2222?per_page=100", compare.RequestUri!.OriginalString);
        Assert.Equal("Bearer t0k", compare.Headers.GetValues("Authorization").Single());
        Assert.Equal("https://api.github.com/repos/acme/shop/commits/bbb2222", files.RequestUri!.OriginalString);
        await Task.CompletedTask;
    }

    [Fact]
    public void GitLab_EncodesProject_AndUsesPrivateToken()
    {
        var cfg = Config(SourceLinkProvider.GitLab, "https://gitlab.com/grp/sub/shop", "t");
        using var compare = SuspectCommitRequestBuilder.BuildCompare(cfg, "aaa1111", "bbb2222")!;
        using var files = SuspectCommitRequestBuilder.BuildCommitFiles(cfg, "bbb2222")!;

        Assert.Equal("https://gitlab.com/api/v4/projects/grp%2Fsub%2Fshop/repository/compare?from=aaa1111&to=bbb2222", compare.RequestUri!.OriginalString);
        Assert.Equal("t", compare.Headers.GetValues("PRIVATE-TOKEN").Single());
        Assert.Equal("https://gitlab.com/api/v4/projects/grp%2Fsub%2Fshop/repository/commits/bbb2222/diff?per_page=100", files.RequestUri!.OriginalString);
    }

    [Fact]
    public async Task AzureDevOps_PostsCommitsBatchWithCommitVersions()
    {
        var cfg = Config(SourceLinkProvider.AzureDevOps, "https://dev.azure.com/org/proj/_git/shop", "pat");
        using var compare = SuspectCommitRequestBuilder.BuildCompare(cfg, "aaa1111", "main")!;
        using var files = SuspectCommitRequestBuilder.BuildCommitFiles(cfg, "bbb2222")!;

        Assert.Equal(HttpMethod.Post, compare.Method);
        Assert.Equal("https://dev.azure.com/org/proj/_apis/git/repositories/shop/commitsbatch?$top=100&api-version=7.1", compare.RequestUri!.OriginalString);
        Assert.Equal("""{"itemVersion":{"version":"main","versionType":"branch"},"compareVersion":{"version":"aaa1111","versionType":"commit"}}""", await compare.Content!.ReadAsStringAsync());
        Assert.StartsWith("Basic ", compare.Headers.GetValues("Authorization").Single());
        Assert.Equal("https://dev.azure.com/org/proj/_apis/git/repositories/shop/commits/bbb2222/changes?api-version=7.1", files.RequestUri!.OriginalString);
    }

    [Fact]
    public void UnparseableRepoUrl_ReturnsNull()
    {
        Assert.Null(SuspectCommitRequestBuilder.BuildCompare(Config(SourceLinkProvider.GitHub, "https://github.com/acme"), "a", "b"));
        Assert.Null(SuspectCommitRequestBuilder.BuildCommitFiles(Config(SourceLinkProvider.AzureDevOps, "https://dev.azure.com/org/shop"), "a"));
    }

    [Theory]
    [InlineData(SourceLinkProvider.GitHub, "https://github.com/acme/shop/commit/abc", "https://github.com/acme/shop/compare/a...b")]
    [InlineData(SourceLinkProvider.GitLab, "https://gitlab.com/acme/shop/-/commit/abc", "https://gitlab.com/acme/shop/-/compare/a...b")]
    public void WebUrls(SourceLinkProvider provider, string commit, string compare)
    {
        var cfg = Config(provider, provider == SourceLinkProvider.GitHub ? "https://github.com/acme/shop/" : "https://gitlab.com/acme/shop");

        Assert.Equal(commit, SuspectCommitRequestBuilder.CommitUrl(cfg, "abc"));
        Assert.Equal(compare, SuspectCommitRequestBuilder.CompareUrl(cfg, "a", "b"));
    }

    [Fact]
    public void ParseGitHubCompare_ReadsCommitsAndCompleteFileList()
    {
        const string json = """
            {"total_commits":2,
             "commits":[
               {"sha":"c1","commit":{"message":"Fix cart\n\nlong body","author":{"name":"Ada","date":"2026-10-01T10:00:00Z"}}},
               {"sha":"c2","commit":{"message":"Rename","author":{"name":"Bob","date":"2026-10-02T10:00:00Z"}}}],
             "files":[{"filename":"src/Cart.cs"},{"filename":"src/New.cs","previous_filename":"src/Old.cs"}]}
            """;

        var compare = SuspectCommitParser.ParseCompare(SourceLinkProvider.GitHub, json);

        Assert.Equal(2, compare.Total);
        Assert.Equal(("c1", "Fix cart", "Ada"), (compare.Commits[0].Sha, compare.Commits[0].Message, compare.Commits[0].Author));
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 10, 0, 0, TimeSpan.Zero), compare.Commits[1].Date);
        Assert.Contains("src/old.cs", compare.ChangedPaths!);
    }

    [Fact]
    public void ParseGitHubCompare_LargeFileList_IsNotTrusted()
    {
        var files = string.Join(',', Enumerable.Range(0, 120).Select(i => $$"""{"filename":"f{{i}}.cs"}"""));

        var json = "{\"total_commits\":1,\"commits\":[{\"sha\":\"c1\",\"commit\":{\"message\":\"m\",\"author\":{\"name\":\"a\"}}}],\"files\":[" + files + "]}";
        var compare = SuspectCommitParser.ParseCompare(SourceLinkProvider.GitHub, json);

        Assert.Null(compare.ChangedPaths);
    }

    [Fact]
    public void ParseGitLabCompare_TimeoutMeansUnknownFiles()
    {
        const string json = """
            {"compare_timeout":true,"commits":[{"id":"c1","title":"Fix","author_name":"Ada","authored_date":"2026-10-01T10:00:00.000+00:00"}],"diffs":[{"new_path":"a.cs","old_path":"a.cs"}]}
            """;

        var compare = SuspectCommitParser.ParseCompare(SourceLinkProvider.GitLab, json);

        Assert.Equal("c1", compare.Commits.Single().Sha);
        Assert.Null(compare.ChangedPaths);
    }

    [Fact]
    public void ParseAzureCompare_ListsCommitsOnly()
    {
        const string json = """{"count":1,"value":[{"commitId":"c1","comment":"Fix","author":{"name":"Ada","date":"2026-10-01T10:00:00Z"}}]}""";

        var compare = SuspectCommitParser.ParseCompare(SourceLinkProvider.AzureDevOps, json);

        Assert.Equal("Ada", compare.Commits.Single().Author);
        Assert.Null(compare.ChangedPaths);
    }

    [Fact]
    public void ParseCommitPaths_PerProvider()
    {
        Assert.Contains("src/a.cs", SuspectCommitParser.ParseCommitPaths(SourceLinkProvider.GitHub, """{"files":[{"filename":"src/A.cs"}]}"""));
        Assert.Contains("old.cs", SuspectCommitParser.ParseCommitPaths(SourceLinkProvider.GitLab, """[{"new_path":"new.cs","old_path":"old.cs"}]"""));
        Assert.Contains("src/a.cs", SuspectCommitParser.ParseCommitPaths(SourceLinkProvider.AzureDevOps, """{"changes":[{"item":{"path":"/src/a.cs"}}]}"""));
    }

    [Fact]
    public void Rank_PrefersThrowSite_ThenNewest_AndDropsNonMatching()
    {
        var older = new HostCommit("older", "touches caller", "a", new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = new HostCommit("newer", "touches throw site", "b", new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero));
        var both = new HostCommit("both", "touches both", "c", new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero));
        var none = new HostCommit("none", "docs", "d", new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero));
        var changed = new Dictionary<string, IReadOnlySet<string>>
        {
            ["older"] = new HashSet<string> { "src/Caller.cs" },
            ["newer"] = new HashSet<string> { "src/Throw.cs" },
            ["both"] = new HashSet<string> { "src/Throw.cs", "src/Caller.cs" },
            ["none"] = new HashSet<string> { "README.md" },
        };

        var ranked = SuspectCommitParser.Rank([older, newer, both, none], changed, ["src/Throw.cs", "src/Caller.cs"]);

        Assert.Equal(["both", "newer", "older"], ranked.Select(r => r.Commit.Sha));
        Assert.Equal(["src/Throw.cs", "src/Caller.cs"], ranked[0].Matched);
    }
}
