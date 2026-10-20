using Flare.Api.Errors;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Errors;

public class ErrorIssueRekeyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static ErrorIssue Issue(string type, string message, ErrorIssueStatus status, int minutesAgo, string assignee = "") => new()
    {
        Id = ErrorIssueFingerprint.Compute(type, message),
        ExceptionType = type,
        ExceptionMessage = message,
        Status = status,
        Assignee = assignee,
        StatusChangedAt = T0.AddMinutes(-minutesAgo),
        KnownVersions = [],
        CreatedAt = T0.AddMinutes(-minutesAgo),
        UpdatedAt = T0.AddMinutes(-minutesAgo),
    };

    [Fact]
    public void UnchangedIssues_NeedNoWrites()
    {
        var issue = Issue("T", "m", ErrorIssueStatus.Resolved, 5);

        var plan = ErrorIssueRekey.Plan([issue], new Dictionary<string, string>(), T0);

        Assert.Empty(plan.Writes);
        Assert.Empty(plan.Tombstones);
    }

    [Fact]
    public void AMovedIssue_IsRewrittenUnderTheNewIdAndTheOldOneTombstoned()
    {
        var issue = Issue("T", "old", ErrorIssueStatus.Ignored, 5, assignee: "sam");

        var plan = ErrorIssueRekey.Plan([issue], new Dictionary<string, string> { [issue.Id] = "new" }, T0);

        var write = Assert.Single(plan.Writes);
        Assert.Equal(ErrorIssueFingerprint.Compute("T", "new"), write.Id);
        Assert.Equal("new", write.ExceptionMessage);
        Assert.Equal(ErrorIssueStatus.Ignored, write.Status);
        Assert.Equal("sam", write.Assignee);
        Assert.Equal(T0, write.UpdatedAt);
        Assert.Equal([issue.Id], plan.Tombstones);
    }

    [Fact]
    public void IssuesFoldedIntoOneGroup_KeepTheMostRecentlyUpdatedState()
    {
        var older = Issue("T", "a", ErrorIssueStatus.Resolved, 60);
        var newer = Issue("T", "b", ErrorIssueStatus.Ignored, 5);
        var map = new Dictionary<string, string> { [older.Id] = "g", [newer.Id] = "g" };

        var plan = ErrorIssueRekey.Plan([older, newer], map, T0);

        var write = Assert.Single(plan.Writes);
        Assert.Equal(ErrorIssueStatus.Ignored, write.Status);
        Assert.Equal(ErrorIssueFingerprint.Compute("T", "g"), write.Id);
        Assert.Equal(2, plan.Tombstones.Count);
    }

    [Fact]
    public void AnIssueAlreadyUnderTheTargetKey_WinsWhenItIsTheNewest_AndIsNotRewritten()
    {
        var target = Issue("T", "g", ErrorIssueStatus.Resolved, 1);
        var folded = Issue("T", "a", ErrorIssueStatus.Ignored, 60);

        var plan = ErrorIssueRekey.Plan([target, folded], new Dictionary<string, string> { [folded.Id] = "g" }, T0);

        Assert.Empty(plan.Writes);
        Assert.Equal([folded.Id], plan.Tombstones);
    }

    [Fact]
    public void GroupsOfDifferentTypes_NeverFold()
    {
        var a = Issue("A", "m", ErrorIssueStatus.Resolved, 5);
        var b = Issue("B", "m", ErrorIssueStatus.Resolved, 5);

        var plan = ErrorIssueRekey.Plan([a, b], new Dictionary<string, string>(), T0);

        Assert.Empty(plan.Writes);
        Assert.Empty(plan.Tombstones);
    }
}
