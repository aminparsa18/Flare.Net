using Xunit;
using Flare.Api.Alerting;

namespace Flare.Api.Tests.Alerting;

public class CredentialExpiryPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly int[] Warn = [14, 3];

    [Theory]
    [InlineData(30, null)]
    [InlineData(14, 14)]
    [InlineData(10, 14)]
    [InlineData(3, 3)]
    [InlineData(2, 3)]
    [InlineData(0, 3)]
    public void DueThreshold_IsTheSmallestThresholdCoveringTheTimeLeft(int daysLeft, int? expected) =>
        Assert.Equal(expected, CredentialExpiryPolicy.DueThreshold(Now.AddDays(daysLeft).AddHours(daysLeft == 0 ? 5 : 0), Now, Warn));

    [Fact]
    public void DueThreshold_IsNullOnceExpired() =>
        Assert.Null(CredentialExpiryPolicy.DueThreshold(Now.AddMinutes(-1), Now, Warn));

    [Fact]
    public void DedupKey_ChangesWhenTheExpiryIsExtended()
    {
        var id = Guid.NewGuid();
        Assert.NotEqual(
            CredentialExpiryPolicy.DedupKey("pat", id, Now.AddDays(2), 3),
            CredentialExpiryPolicy.DedupKey("pat", id, Now.AddDays(2.5), 3));
    }

    [Fact]
    public void BuildRule_NamesTheCredentialOwnerAndDeadline_AndNeutralisesTemplateBraces()
    {
        var rule = CredentialExpiryPolicy.BuildRule("Personal access token", "ci {{x}}", "alice", Guid.NewGuid(), Now.AddDays(2), Now, "https://flare.example.com/", "/settings/access-tokens");

        Assert.Equal("Personal access token \"ci { {x}}\" expires in 2 days", rule.NotificationTitleTemplate);
        Assert.Contains("owned by alice", rule.NotificationBodyTemplate);
        Assert.Contains("2026-10-14 00:00 UTC", rule.NotificationBodyTemplate);
        Assert.EndsWith("https://flare.example.com/settings/access-tokens", rule.NotificationBodyTemplate);
    }
}
