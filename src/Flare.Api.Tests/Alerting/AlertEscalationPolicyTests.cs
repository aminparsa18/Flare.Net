using Xunit;
using Flare.Api.Alerting;
using Flare.Api.Model;

namespace Flare.Api.Tests.Alerting;

public class AlertEscalationPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static AlertRule Rule(int minutes = 15, int channels = 1) => new()
    {
        Id = Guid.NewGuid(),
        Name = "r",
        Condition = new LogFilter(),
        Threshold = new AlertThreshold { Count = 1 },
        WindowSeconds = 60,
        CreatedAt = Now,
        UpdatedAt = Now,
        EscalateAfterMinutes = minutes,
        EscalationChannelIds = [.. Enumerable.Range(0, channels).Select(_ => Guid.NewGuid())],
    };

    private static AlertFiringState State(int notifiedMinutesAgo = 20, bool notified = true) =>
        new(Now.AddMinutes(-1), notified) { IncidentNotifiedAt = Now.AddMinutes(-notifiedMinutesAgo) };

    [Fact]
    public void Due_once_the_incident_is_old_enough_and_unacked() =>
        Assert.True(AlertEscalationPolicy.IsDue(Rule(), State(), false, Now));

    [Fact]
    public void Not_due_before_the_delay() =>
        Assert.False(AlertEscalationPolicy.IsDue(Rule(), State(notifiedMinutesAgo: 14), false, Now));

    [Fact]
    public void Off_when_disabled_or_without_channels()
    {
        Assert.False(AlertEscalationPolicy.IsDue(Rule(minutes: 0), State(), false, Now));
        Assert.False(AlertEscalationPolicy.IsDue(Rule(channels: 0), State(), false, Now));
    }

    [Fact]
    public void Not_due_when_nobody_was_paged_or_already_escalated()
    {
        Assert.False(AlertEscalationPolicy.IsDue(Rule(), State(notified: false), false, Now));
        Assert.False(AlertEscalationPolicy.IsDue(Rule(), State() with { EscalatedAt = Now.AddMinutes(-2) }, false, Now));
    }

    [Fact]
    public void Ack_stops_it_but_snooze_does_not()
    {
        var ack = new AlertAck(Guid.NewGuid(), Now.AddMinutes(-5), "a", AlertAckKind.Ack, null, "");
        var snooze = ack with { Kind = AlertAckKind.Snooze, SnoozedUntil = Now.AddHours(1) };
        Assert.False(AlertEscalationPolicy.IsDue(Rule(), State() with { Ack = ack }, false, Now));
        Assert.True(AlertEscalationPolicy.IsDue(Rule(), State() with { Ack = snooze }, false, Now));
    }

    [Fact]
    public void Maintenance_window_defers_it() =>
        Assert.False(AlertEscalationPolicy.IsDue(Rule(), State(), true, Now));

    [Fact]
    public void Validate_checks_bounds_and_channels()
    {
        var id = Guid.NewGuid();
        Assert.Null(AlertEscalationPolicy.Validate(0, null));
        Assert.Null(AlertEscalationPolicy.Validate(15, [id]));
        Assert.NotNull(AlertEscalationPolicy.Validate(-1, [id]));
        Assert.NotNull(AlertEscalationPolicy.Validate(AlertEscalationPolicy.MaxEscalateAfterMinutes + 1, [id]));
        Assert.NotNull(AlertEscalationPolicy.Validate(15, []));
        Assert.NotNull(AlertEscalationPolicy.Validate(15, [id, id]));
    }
}
