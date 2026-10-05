using Flare.Api.Errors;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Errors;

public class ErrorIssueEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static ErrorIssue Issue(ErrorIssueStatus status, string[]? known = null, DateTimeOffset? until = null, int? untilCount = null) => new()
    {
        Id = "id",
        ExceptionType = "T",
        ExceptionMessage = "m",
        Status = status,
        StatusChangedAt = Now.AddHours(-1),
        IgnoreUntil = until,
        IgnoreUntilOccurrences = untilCount,
        KnownVersions = known ?? [],
        CreatedAt = Now.AddHours(-1),
        UpdatedAt = Now.AddHours(-1),
    };

    [Fact]
    public void Open_IsUnchanged() =>
        Assert.Equal(ErrorIssueStatus.Open, ErrorIssueEvaluator.Evaluate(Issue(ErrorIssueStatus.Open), [], Now).Status);

    [Fact]
    public void Ignored_WithoutLimits_StaysIgnored()
    {
        var result = ErrorIssueEvaluator.Evaluate(Issue(ErrorIssueStatus.Ignored), [], Now);
        Assert.Equal(ErrorIssueStatus.Ignored, result.Status);
        Assert.True(ErrorIssueEvaluator.IsMuted(result));
    }

    [Fact]
    public void Ignored_PastIgnoreUntil_ReadsOpen() =>
        Assert.Equal(ErrorIssueStatus.Open, ErrorIssueEvaluator.Evaluate(Issue(ErrorIssueStatus.Ignored, until: Now.AddMinutes(-1)), [], Now).Status);

    [Fact]
    public void Ignored_BeforeIgnoreUntil_StaysIgnored() =>
        Assert.Equal(ErrorIssueStatus.Ignored, ErrorIssueEvaluator.Evaluate(Issue(ErrorIssueStatus.Ignored, until: Now.AddMinutes(1)), [], Now).Status);

    [Fact]
    public void Ignored_CountLimitReached_ReadsOpen()
    {
        var result = ErrorIssueEvaluator.Evaluate(Issue(ErrorIssueStatus.Ignored, untilCount: 5), [new("1.0", 3), new("1.1", 2)], Now);
        Assert.Equal(ErrorIssueStatus.Open, result.Status);
        Assert.Equal(5, result.OccurrencesSinceChange);
    }

    [Fact]
    public void Ignored_CountLimitNotReached_StaysIgnoredAndReportsProgress()
    {
        var result = ErrorIssueEvaluator.Evaluate(Issue(ErrorIssueStatus.Ignored, untilCount: 5), [new("1.0", 4)], Now);
        Assert.Equal(ErrorIssueStatus.Ignored, result.Status);
        Assert.Equal(4, result.OccurrencesSinceChange);
    }

    [Fact]
    public void Resolved_NoRecurrence_StaysResolved() =>
        Assert.Equal(ErrorIssueStatus.Resolved, ErrorIssueEvaluator.Evaluate(Issue(ErrorIssueStatus.Resolved, ["1.0"]), [], Now).Status);

    [Fact]
    public void Resolved_RecurrenceInKnownVersion_StaysResolved() =>
        Assert.Equal(ErrorIssueStatus.Resolved, ErrorIssueEvaluator.Evaluate(Issue(ErrorIssueStatus.Resolved, ["1.0"]), [new("1.0", 7)], Now).Status);

    [Fact]
    public void Resolved_RecurrenceInNewVersion_ReadsRegressed()
    {
        var result = ErrorIssueEvaluator.Evaluate(Issue(ErrorIssueStatus.Resolved, ["1.0"]), [new("1.0", 2), new("1.1", 1)], Now);
        Assert.Equal(ErrorIssueStatus.Regressed, result.Status);
        Assert.Equal("1.1", result.RegressedVersion);
    }

    [Fact]
    public void Resolved_NoKnownVersions_AnyRecurrenceRegresses() =>
        Assert.Equal(ErrorIssueStatus.Regressed, ErrorIssueEvaluator.Evaluate(Issue(ErrorIssueStatus.Resolved, [""]), [new("", 1)], Now).Status);

    [Fact]
    public void Resolved_ZeroCountEvidence_IsIgnored() =>
        Assert.Equal(ErrorIssueStatus.Resolved, ErrorIssueEvaluator.Evaluate(Issue(ErrorIssueStatus.Resolved, ["1.0"]), [new("2.0", 0)], Now).Status);

    [Fact]
    public void Regressed_IsNotMuted() =>
        Assert.False(ErrorIssueEvaluator.IsMuted(Issue(ErrorIssueStatus.Regressed)));
}

public class ErrorIssueFingerprintTests
{
    [Fact]
    public void Compute_IsStableAnd32Hex()
    {
        var id = ErrorIssueFingerprint.Compute("T", "m");
        Assert.Equal(id, ErrorIssueFingerprint.Compute("T", "m"));
        Assert.Equal(32, id.Length);
    }

    [Fact]
    public void Compute_DistinguishesTypeFromMessageBoundary() =>
        Assert.NotEqual(ErrorIssueFingerprint.Compute("ab", "c"), ErrorIssueFingerprint.Compute("a", "bc"));
}

public class ErrorIssueRequestValidationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MissingType_Rejected() =>
        Assert.NotNull(new ErrorIssueRequest { ExceptionType = " " }.Validate(Now));

    [Fact]
    public void Regressed_Rejected() =>
        Assert.NotNull(new ErrorIssueRequest { ExceptionType = "T", Status = ErrorIssueStatus.Regressed }.Validate(Now));

    [Fact]
    public void IgnoreLimits_RequireIgnoredStatus() =>
        Assert.NotNull(new ErrorIssueRequest { ExceptionType = "T", Status = ErrorIssueStatus.Resolved, IgnoreUntilOccurrences = 5 }.Validate(Now));

    [Fact]
    public void IgnoreUntil_MustBeFuture() =>
        Assert.NotNull(new ErrorIssueRequest { ExceptionType = "T", Status = ErrorIssueStatus.Ignored, IgnoreUntil = Now }.Validate(Now));

    [Fact]
    public void IgnoreOccurrences_OutOfRange_Rejected() =>
        Assert.NotNull(new ErrorIssueRequest { ExceptionType = "T", Status = ErrorIssueStatus.Ignored, IgnoreUntilOccurrences = 0 }.Validate(Now));

    [Fact]
    public void ValidIgnore_Accepted() =>
        Assert.Null(new ErrorIssueRequest { ExceptionType = "T", Status = ErrorIssueStatus.Ignored, IgnoreUntil = Now.AddDays(1), IgnoreUntilOccurrences = 10 }.Validate(Now));

    [Fact]
    public void AssigneeOnly_Accepted() =>
        Assert.Null(new ErrorIssueRequest { ExceptionType = "T", Assignee = "alice" }.Validate(Now));
}
