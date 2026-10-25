using Flare.Api.Model;
using Flare.Identity.IssueTrackers;
using Xunit;

namespace Flare.Api.Tests.Model;

public class IssueTrackerDtoTests
{
    private static IssueTrackerDto Dto(string siteUrl = "https://acme.atlassian.net", string projectId = "10000", string issueTypeId = "10001") => new()
    {
        ServiceName = "checkout-api",
        Provider = IssueTrackerProvider.Jira,
        SiteUrl = siteUrl,
        ProjectId = projectId,
        IssueTypeId = issueTypeId
    };

    [Fact]
    public void Validate_WithAFullConfig_ReturnsNull() => Assert.Null(Dto().Validate());

    [Fact]
    public void Validate_WithoutAnIssueType_ReturnsNull() => Assert.Null(Dto(issueTypeId: "").Validate());

    [Theory]
    [InlineData("acme.atlassian.net")]
    [InlineData("javascript:alert(1)")]
    [InlineData("")]
    public void Validate_WithANonHttpSiteUrl_ReturnsAnError(string siteUrl) => Assert.NotNull(Dto(siteUrl: siteUrl).Validate());

    [Theory]
    [InlineData("")]
    [InlineData("PROJ")]
    [InlineData("10 000")]
    public void Validate_WithANonNumericProjectId_ReturnsAnError(string projectId) => Assert.NotNull(Dto(projectId: projectId).Validate());

    [Fact]
    public void Validate_WithANonNumericIssueType_ReturnsAnError() => Assert.NotNull(Dto(issueTypeId: "Bug").Validate());

    [Fact]
    public void Validate_AcceptsAnAccountEmailAndAToken() =>
        Assert.Null((Dto() with { AccountEmail = "bot@acme.test", ApiToken = "tok" }).Validate());

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a b@acme.test")]
    public void Validate_WithAMalformedAccountEmail_ReturnsAnError(string email) => Assert.NotNull((Dto() with { AccountEmail = email }).Validate());
}
