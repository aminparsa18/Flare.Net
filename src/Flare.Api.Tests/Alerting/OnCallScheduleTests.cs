using Xunit;
using Flare.Api.Alerting;
using Flare.Api.Model;

namespace Flare.Api.Tests.Alerting;

public class OnCallScheduleTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid A = Guid.NewGuid();
    private static readonly Guid B = Guid.NewGuid();
    private static readonly Guid C = Guid.NewGuid();

    private static OnCallRotation Rotation(int shiftHours = 24, params Guid[] channels) => new()
    {
        Id = Guid.NewGuid(),
        Name = "primary",
        ChannelIds = channels.Length == 0 ? [A, B, C] : channels,
        ShiftHours = shiftHours,
        StartsAt = Start,
        CreatedAt = Start,
        UpdatedAt = Start,
    };

    [Fact]
    public void First_participant_is_on_call_at_the_start()
    {
        var status = OnCallSchedule.Resolve(Rotation(), Start);
        Assert.Equal(A, status.OnCallChannelId);
        Assert.Equal(Start.AddHours(24), status.ShiftEndsAt);
        Assert.Equal(B, status.NextChannelId);
    }

    [Fact]
    public void Hands_over_exactly_at_the_shift_boundary()
    {
        var rotation = Rotation();
        Assert.Equal(A, OnCallSchedule.Resolve(rotation, Start.AddHours(24).AddTicks(-1)).OnCallChannelId);
        Assert.Equal(B, OnCallSchedule.Resolve(rotation, Start.AddHours(24)).OnCallChannelId);
        Assert.Equal(C, OnCallSchedule.Resolve(rotation, Start.AddHours(49)).OnCallChannelId);
    }

    [Fact]
    public void Wraps_around_after_the_last_participant()
    {
        var status = OnCallSchedule.Resolve(Rotation(), Start.AddHours(24 * 3 + 1));
        Assert.Equal(A, status.OnCallChannelId);
        Assert.Equal(Start.AddHours(24 * 4), status.ShiftEndsAt);
        Assert.Equal(B, status.NextChannelId);
    }

    [Fact]
    public void Before_the_start_the_first_participant_is_on_call()
    {
        var status = OnCallSchedule.Resolve(Rotation(), Start.AddDays(-3));
        Assert.Equal(A, status.OnCallChannelId);
        Assert.Equal(Start, status.ShiftEndsAt);
    }

    [Fact]
    public void A_single_participant_is_always_on_call()
    {
        var status = OnCallSchedule.Resolve(Rotation(24, A), Start.AddDays(40));
        Assert.Equal(A, status.OnCallChannelId);
        Assert.Equal(A, status.NextChannelId);
    }

    [Fact]
    public void Escalation_targets_add_the_on_call_channel_without_repeats()
    {
        var fixedChannel = Guid.NewGuid();
        var rotation = Rotation();
        Assert.Equal([fixedChannel, A], OnCallSchedule.EscalationTargets([fixedChannel], rotation, Start));
        Assert.Equal([A], OnCallSchedule.EscalationTargets([A], rotation, Start));
        Assert.Equal([fixedChannel], OnCallSchedule.EscalationTargets([fixedChannel], null, Start));
    }

    [Fact]
    public void Request_validation()
    {
        var ok = new OnCallRotationRequest { Name = "p", ChannelIds = [A, B], ShiftHours = 24, StartsAt = Start };
        Assert.Null(ok.Validate());
        Assert.Null((ok with { ChannelIds = [A, B, A] }).Validate());
        Assert.NotNull((ok with { Name = " " }).Validate());
        Assert.NotNull((ok with { ChannelIds = [] }).Validate());
        Assert.NotNull((ok with { ChannelIds = [Guid.Empty] }).Validate());
        Assert.NotNull((ok with { ShiftHours = 0 }).Validate());
        Assert.NotNull((ok with { ShiftHours = null }).Validate());
        Assert.NotNull((ok with { ShiftHours = OnCallRotationRequest.MaxShiftHours + 1 }).Validate());
    }
}
