using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Errors;

public class ErrorIssueLinkAndOwnershipTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("https://github.com/acme/shop/issues/42")]
    [InlineData("http://tracker.internal/browse/ABC-1")]
    [InlineData("")]
    [InlineData(null)]
    public void IssueUrl_AcceptsHttpUrlsAndClearing(string? url) =>
        Assert.Null(new ErrorIssueRequest { ExceptionType = "T", IssueUrl = url }.Validate(Now));

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://host/issue")]
    [InlineData("not a url")]
    [InlineData("/relative/path")]
    public void IssueUrl_RejectsNonHttpUrls(string url) =>
        Assert.NotNull(new ErrorIssueRequest { ExceptionType = "T", IssueUrl = url }.Validate(Now));

    [Fact]
    public void IssueUrl_RejectsOverlongUrl() =>
        Assert.NotNull(new ErrorIssueRequest { ExceptionType = "T", IssueUrl = "https://x.test/" + new string('a', ErrorIssueRequest.MaxIssueUrlLength) }.Validate(Now));

    [Fact]
    public void OwnershipDocument_AcceptsOrderedRules() =>
        Assert.Null(new ErrorOwnershipRulesDocument
        {
            Rules =
            [
                new() { Kind = ErrorOwnershipKind.Service, Pattern = "payments-*", Owner = "alice" },
                new() { Kind = ErrorOwnershipKind.ExceptionType, Pattern = "*Timeout*", Owner = "bob" },
            ],
        }.Validate());

    [Theory]
    [InlineData("", "alice")]
    [InlineData("svc", " ")]
    public void OwnershipDocument_RequiresPatternAndOwner(string pattern, string owner) =>
        Assert.NotNull(new ErrorOwnershipRulesDocument { Rules = [new() { Kind = ErrorOwnershipKind.Service, Pattern = pattern, Owner = owner }] }.Validate());

    [Fact]
    public void OwnershipDocument_RejectsTooManyRules() =>
        Assert.NotNull(new ErrorOwnershipRulesDocument
        {
            Rules = Enumerable.Range(0, ErrorOwnershipRulesDocument.MaxRules + 1).Select(i => new ErrorOwnershipRule { Kind = ErrorOwnershipKind.Service, Pattern = $"s{i}", Owner = "a" }).ToList(),
        }.Validate());
}
