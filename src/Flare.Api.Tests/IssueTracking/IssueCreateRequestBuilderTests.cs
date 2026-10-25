using System.Text;
using System.Text.Json.Nodes;
using Flare.Api.IssueTracking;
using Flare.Api.Model;
using Flare.Identity.IssueTrackers;
using Flare.Identity.SourceLinks;
using Xunit;

namespace Flare.Api.Tests.IssueTracking;

public class IssueCreateRequestBuilderTests
{
    private static SourceLinkConfig Repo(SourceLinkProvider provider, string url, string? issueToken = "w-t0k", string? readToken = "r-t0k") =>
        new("svc", provider, url, "main", "", readToken, issueToken);

    private static IssueTrackerConfig Jira(string email = "", string? token = "j-t0k", string issueType = "10001") =>
        new("svc", IssueTrackerProvider.Jira, "https://acme.atlassian.net", "10000", issueType, email, token);

    private static JsonNode Body(HttpRequestMessage request) => JsonNode.Parse(request.Content!.ReadAsStringAsync().Result)!;

    [Fact]
    public void GitHub_PostsToIssuesWithTheWriteToken_NotTheReadToken()
    {
        using var r = IssueCreateRequestBuilder.BuildRepoIssue(Repo(SourceLinkProvider.GitHub, "https://github.com/acme/shop.git"), "T", "B")!;

        Assert.Equal(HttpMethod.Post, r.Method);
        Assert.Equal("https://api.github.com/repos/acme/shop/issues", r.RequestUri!.ToString());
        Assert.Equal("Bearer w-t0k", r.Headers.GetValues("Authorization").Single());
        Assert.Equal("T", Body(r)["title"]!.GetValue<string>());
        Assert.Equal("B", Body(r)["body"]!.GetValue<string>());
    }

    [Fact]
    public void GitHubEnterprise_UsesTheHostsApiV3() =>
        Assert.Equal("https://git.corp.example/api/v3/repos/acme/shop/issues",
            IssueCreateRequestBuilder.BuildRepoIssue(Repo(SourceLinkProvider.GitHub, "https://git.corp.example/acme/shop"), "T", "B")!.RequestUri!.ToString());

    [Fact]
    public void GitLab_EncodesTheProjectPath_AndSendsDescription()
    {
        using var r = IssueCreateRequestBuilder.BuildRepoIssue(Repo(SourceLinkProvider.GitLab, "https://gitlab.example.com/group/sub/shop"), "T", "B")!;

        Assert.Equal("https://gitlab.example.com/api/v4/projects/group%2Fsub%2Fshop/issues", r.RequestUri!.OriginalString);
        Assert.Equal("w-t0k", r.Headers.GetValues("PRIVATE-TOKEN").Single());
        Assert.Equal("B", Body(r)["description"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void RepoIssue_WithoutAnIssueToken_IsNull_EvenWithAReadToken(string? issueToken) =>
        Assert.Null(IssueCreateRequestBuilder.BuildRepoIssue(Repo(SourceLinkProvider.GitHub, "https://github.com/acme/shop", issueToken), "T", "B"));

    [Fact]
    public void RepoIssue_ForAzureDevOpsOrAMalformedRepo_IsNull()
    {
        Assert.Null(IssueCreateRequestBuilder.BuildRepoIssue(Repo(SourceLinkProvider.AzureDevOps, "https://dev.azure.com/o/p/_git/r"), "T", "B"));
        Assert.Null(IssueCreateRequestBuilder.BuildRepoIssue(Repo(SourceLinkProvider.GitHub, "https://github.com/acme"), "T", "B"));
    }

    [Fact]
    public void JiraCloud_UsesV3WithBasicAuthAndAnAdfDescription()
    {
        using var r = IssueCreateRequestBuilder.BuildJiraIssue(Jira(email: "bot@acme.test"), "T", "line1\nline2")!;

        Assert.Equal("https://acme.atlassian.net/rest/api/3/issue", r.RequestUri!.ToString());
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("bot@acme.test:j-t0k")), r.Headers.GetValues("Authorization").Single());
        var fields = Body(r)["fields"]!;
        Assert.Equal("10000", fields["project"]!["id"]!.GetValue<string>());
        Assert.Equal("10001", fields["issuetype"]!["id"]!.GetValue<string>());
        Assert.Equal("doc", fields["description"]!["type"]!.GetValue<string>());
        Assert.Equal(2, fields["description"]!["content"]!.AsArray().Count);
    }

    [Fact]
    public void JiraServer_UsesV2WithABearerPatAndATextDescription()
    {
        using var r = IssueCreateRequestBuilder.BuildJiraIssue(Jira(), "T", "plain body")!;

        Assert.Equal("https://acme.atlassian.net/rest/api/2/issue", r.RequestUri!.ToString());
        Assert.Equal("Bearer j-t0k", r.Headers.GetValues("Authorization").Single());
        Assert.Equal("plain body", Body(r)["fields"]!["description"]!.GetValue<string>());
    }

    [Fact]
    public void Jira_WithoutATokenOrIssueType_IsNull()
    {
        Assert.Null(IssueCreateRequestBuilder.BuildJiraIssue(Jira(token: null), "T", "B"));
        Assert.Null(IssueCreateRequestBuilder.BuildJiraIssue(Jira(issueType: ""), "T", "B"));
    }

    [Theory]
    [InlineData(IssueTrackerTarget.GitHub, """{"html_url":"https://github.com/acme/shop/issues/7"}""", "https://github.com/acme/shop", "https://github.com/acme/shop/issues/7")]
    [InlineData(IssueTrackerTarget.GitLab, """{"web_url":"https://gitlab.example.com/g/shop/-/issues/3"}""", "https://gitlab.example.com/g/shop", "https://gitlab.example.com/g/shop/-/issues/3")]
    [InlineData(IssueTrackerTarget.Jira, """{"id":"1","key":"OPS-42","self":"x"}""", "https://acme.atlassian.net/", "https://acme.atlassian.net/browse/OPS-42")]
    public void ParseIssueUrl_ReadsTheHostsLink(IssueTrackerTarget target, string body, string configured, string expected) =>
        Assert.Equal(expected, IssueCreateRequestBuilder.ParseIssueUrl(target, body, configured));

    [Theory]
    [InlineData(IssueTrackerTarget.GitHub, """{"html_url":"https://evil.example/x"}""")]
    [InlineData(IssueTrackerTarget.GitHub, """{"html_url":"javascript:alert(1)"}""")]
    [InlineData(IssueTrackerTarget.GitHub, """{"number":7}""")]
    [InlineData(IssueTrackerTarget.GitHub, "not json")]
    [InlineData(IssueTrackerTarget.Jira, """{"key":"../../etc"}""")]
    public void ParseIssueUrl_RejectsAnythingNotOnTheConfiguredHost(IssueTrackerTarget target, string body) =>
        Assert.Null(IssueCreateRequestBuilder.ParseIssueUrl(target, body, "https://github.com/acme/shop"));
}
