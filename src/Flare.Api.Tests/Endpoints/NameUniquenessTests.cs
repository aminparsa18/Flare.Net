using Flare.Api.Endpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Xunit;

namespace Flare.Api.Tests.Endpoints;

public class NameUniquenessTests
{
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();

    private static (Guid, string)[] Existing => [(A, "Oncall Slack"), (B, "email")];

    [Fact]
    public void FreeName_IsAccepted() =>
        Assert.Null(NameUniqueness.Conflict(Existing, "channel", "pagerduty"));

    [Theory]
    [InlineData("Oncall Slack")]
    [InlineData("oncall slack")]
    [InlineData("  ONCALL SLACK  ")]
    public void TakenName_IsAConflict_IgnoringCaseAndPadding(string name)
    {
        var result = NameUniqueness.Conflict(Existing, "channel", name);

        Assert.Equal(409, Assert.IsType<ProblemHttpResult>(result).StatusCode);
    }

    [Fact]
    public void RenamingAnItemToItsOwnName_IsAccepted() =>
        Assert.Null(NameUniqueness.Conflict(Existing, "channel", "oncall slack", exceptId: A));

    [Fact]
    public void RenamingOntoAnotherItemsName_IsAConflict() =>
        Assert.NotNull(NameUniqueness.Conflict(Existing, "channel", "email", exceptId: A, currentName: "Oncall Slack"));

    [Fact]
    public void AnUnchangedName_IsAcceptedEvenWhenLegacyDuplicatesExist() =>
        Assert.Null(NameUniqueness.Conflict([(A, "dup"), (B, "dup")], "channel", "dup", exceptId: A, currentName: "dup"));
}
