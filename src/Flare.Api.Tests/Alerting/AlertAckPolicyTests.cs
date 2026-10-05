using Flare.Api.Alerting;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.Alerting;

public sealed class AlertAckPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Rule = Guid.NewGuid();

    private static AlertAck Ack(AlertAckKind kind, DateTimeOffset at, DateTimeOffset? until = null) => new(Rule, at, "sam", kind, until, "");

    [Fact]
    public void Effective_NullOrClear_IsNull()
    {
        Assert.Null(AlertAckPolicy.Effective(null, null));
        Assert.Null(AlertAckPolicy.Effective(Ack(AlertAckKind.Clear, Now), null));
    }

    [Fact]
    public void Effective_AckBeforeLastResolution_BelongsToAPreviousIncident()
    {
        var ack = Ack(AlertAckKind.Ack, Now.AddHours(-2));
        Assert.Null(AlertAckPolicy.Effective(ack, Now.AddHours(-1)));
        Assert.Equal(ack, AlertAckPolicy.Effective(ack, Now.AddHours(-3)));
        Assert.Equal(ack, AlertAckPolicy.Effective(ack, null));
    }

    [Fact]
    public void Silences_AckAlways_SnoozeUntilExpiry()
    {
        Assert.False(AlertAckPolicy.Silences(null, Now));
        Assert.True(AlertAckPolicy.Silences(Ack(AlertAckKind.Ack, Now.AddDays(-1)), Now));
        Assert.True(AlertAckPolicy.Silences(Ack(AlertAckKind.Snooze, Now, Now.AddMinutes(30)), Now));
        Assert.False(AlertAckPolicy.Silences(Ack(AlertAckKind.Snooze, Now.AddHours(-1), Now.AddMinutes(-1)), Now));
        Assert.False(AlertAckPolicy.Silences(Ack(AlertAckKind.Snooze, Now, null), Now));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(0, false)]
    [InlineData(-5, false)]
    [InlineData(60, true)]
    [InlineData(AlertAckPolicy.MaxSnoozeMinutes, true)]
    [InlineData(AlertAckPolicy.MaxSnoozeMinutes + 1, false)]
    public void ValidateSnooze_BoundsDuration(int? minutes, bool valid) =>
        Assert.Equal(valid, AlertAckPolicy.ValidateSnooze(minutes) is null);
}
