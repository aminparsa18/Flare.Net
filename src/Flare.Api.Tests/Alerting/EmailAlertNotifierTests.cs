using Flare.Api.Alerting;
using Xunit;

namespace Flare.Api.Tests.Alerting;

/// <summary>
/// Covers <see cref="EmailAlertNotifier.SplitRecipients"/> - the one piece of pure logic
/// in <see cref="EmailAlertNotifier"/>, pulled out specifically so it's unit-testable
/// without real SMTP I/O (same "real I/O stays untested-against-a-fake" precedent
/// <see cref="Flare.Api.Query.AlertQueryService"/>/<see cref="AlertEvaluationWorker"/>
/// follow, per <c>Flare.Api/README.md</c>'s "Tests" section).
/// </summary>
public class EmailAlertNotifierTests
{
    [Fact]
    public void SingleAddress_ReturnsOneEntry()
    {
        var result = EmailAlertNotifier.SplitRecipients("oncall@example.com");

        Assert.Equal(["oncall@example.com"], result);
    }

    [Fact]
    public void CommaSeparated_SplitsIntoMultiple()
    {
        var result = EmailAlertNotifier.SplitRecipients("a@example.com,b@example.com");

        Assert.Equal(["a@example.com", "b@example.com"], result);
    }

    [Fact]
    public void SemicolonSeparated_SplitsIntoMultiple()
    {
        var result = EmailAlertNotifier.SplitRecipients("a@example.com;b@example.com");

        Assert.Equal(["a@example.com", "b@example.com"], result);
    }

    [Fact]
    public void MixedSeparatorsAndWhitespace_AreTrimmed()
    {
        var result = EmailAlertNotifier.SplitRecipients(" a@example.com ,  b@example.com; c@example.com ");

        Assert.Equal(["a@example.com", "b@example.com", "c@example.com"], result);
    }

    [Fact]
    public void EmptyEntriesFromDoubledSeparators_AreDropped()
    {
        var result = EmailAlertNotifier.SplitRecipients("a@example.com,,;b@example.com");

        Assert.Equal(["a@example.com", "b@example.com"], result);
    }

    [Fact]
    public void EmptyString_ReturnsNoEntries()
    {
        var result = EmailAlertNotifier.SplitRecipients("");

        Assert.Empty(result);
    }
}
