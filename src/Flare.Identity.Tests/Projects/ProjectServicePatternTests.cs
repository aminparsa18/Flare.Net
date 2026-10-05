using Flare.Identity.Projects;
using Xunit;

namespace Flare.Identity.Tests.Projects;

public class ProjectServicePatternTests
{
    [Theory]
    [InlineData("checkout", "checkout", true)]
    [InlineData("checkout", "checkout-api", false)]
    [InlineData("checkout-*", "checkout-api", true)]
    [InlineData("checkout-*", "checkout-", true)]
    [InlineData("checkout-*", "payments-api", false)]
    [InlineData("Checkout", "checkout", false)]
    public void Matches_ExactOrTrailingStarPrefix(string pattern, string service, bool expected) =>
        Assert.Equal(expected, ProjectServicePattern.Matches(pattern, service));

    [Theory]
    [InlineData("checkout", null)]
    [InlineData("checkout-*", null)]
    [InlineData("", "empty")]
    [InlineData(" a", "trimmed")]
    [InlineData("a*b", "last")]
    [InlineData("*", "match every")]
    public void Validate_FlagsBadPatterns(string pattern, string? errorFragment)
    {
        var error = ProjectServicePattern.Validate(pattern);
        if (errorFragment is null)
        {
            Assert.Null(error);
        }
        else
        {
            Assert.Contains(errorFragment, error, StringComparison.OrdinalIgnoreCase);
        }
    }
}
