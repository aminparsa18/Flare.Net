using Flare.Api.Source;
using Flare.Identity.SourceLinks;
using Xunit;

namespace Flare.Api.Tests.Source;

public class SourceFetchRequestBuilderTests
{
    private static SourceLinkConfig Config(SourceLinkProvider provider, string url, string? token = null) =>
        new("svc", provider, url, "main", "", token);

    [Fact]
    public void GitHub_UsesContentsApiWithBearerToken()
    {
        using var r = SourceFetchRequestBuilder.Build(Config(SourceLinkProvider.GitHub, "https://github.com/acme/shop", "t0k"), "abc1234", true, "src/A B.cs")!;

        Assert.Equal("https://api.github.com/repos/acme/shop/contents/src/A%20B.cs?ref=abc1234", r.RequestUri!.OriginalString);
        Assert.Equal("Bearer t0k", r.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public void GitHubEnterprise_UsesTheHostsApiV3()
    {
        using var r = SourceFetchRequestBuilder.Build(Config(SourceLinkProvider.GitHub, "https://git.corp.example/acme/shop"), "main", false, "a.cs")!;

        Assert.StartsWith("https://git.corp.example/api/v3/repos/acme/shop/contents/a.cs", r.RequestUri!.ToString());
        Assert.False(r.Headers.Contains("Authorization"));
    }

    [Fact]
    public void GitLab_EncodesProjectPathAndFilePath()
    {
        using var r = SourceFetchRequestBuilder.Build(Config(SourceLinkProvider.GitLab, "https://gitlab.com/grp/sub/shop", "t"), "main", false, "src/a.cs")!;

        Assert.Equal("https://gitlab.com/api/v4/projects/grp%2Fsub%2Fshop/repository/files/src%2Fa.cs/raw?ref=main", r.RequestUri!.OriginalString);
        Assert.Equal("t", r.Headers.GetValues("PRIVATE-TOKEN").Single());
    }

    [Fact]
    public void AzureDevOps_UsesItemsApiWithBasicPat_AndCommitVersionType()
    {
        using var r = SourceFetchRequestBuilder.Build(Config(SourceLinkProvider.AzureDevOps, "https://dev.azure.com/org/proj/_git/repo", "pat"), "abc1234", true, "src/a.cs")!;

        var url = r.RequestUri!.ToString();
        Assert.StartsWith("https://dev.azure.com/org/proj/_apis/git/repositories/repo/items?", url);
        Assert.Contains("versionDescriptor.versionType=commit", url);
        Assert.Equal("Basic " + Convert.ToBase64String(":pat"u8.ToArray()), r.Headers.GetValues("Authorization").Single());
    }

    [Fact]
    public void RepoUrlThatDoesNotFitTheProvider_ReturnsNull() =>
        Assert.Null(SourceFetchRequestBuilder.Build(Config(SourceLinkProvider.AzureDevOps, "https://github.com/acme/shop"), "main", false, "a.cs"));

    [Theory]
    [InlineData("src/a.cs", true)]
    [InlineData("../etc/passwd", false)]
    [InlineData("/abs/a.cs", false)]
    [InlineData("a//b.cs", false)]
    [InlineData("a\\b.cs", false)]
    public void IsSafePath_RejectsTraversalAndAbsolute(string path, bool expected) =>
        Assert.Equal(expected, SourceSnippetService.IsSafePath(path));

    [Theory]
    [InlineData("abc1234", true)]
    [InlineData("release/1.2", true)]
    [InlineData("a b", false)]
    [InlineData("a?b=c", false)]
    public void IsSafeRef_AllowsOnlyRefCharacters(string gitRef, bool expected) =>
        Assert.Equal(expected, SourceSnippetService.IsSafeRef(gitRef));
}
