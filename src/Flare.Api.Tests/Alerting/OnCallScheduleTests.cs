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
    public void An_active_override_replaces_the_scheduled_participant()
    {
        var rotation = Rotation() with { Overrides = [new OnCallOverride(Start.AddHours(6), Start.AddHours(12), C)] };

        var during = OnCallSchedule.Resolve(rotation, Start.AddHours(8));
        Assert.Equal(C, during.OnCallChannelId);
        Assert.True(during.IsOverride);
        Assert.Equal(Start.AddHours(12), during.ShiftEndsAt);
        Assert.Equal(A, during.NextChannelId);

        // Start inclusive, end exclusive; outside it the schedule applies again.
        Assert.Equal(C, OnCallSchedule.Resolve(rotation, Start.AddHours(6)).OnCallChannelId);
        var after = OnCallSchedule.Resolve(rotation, Start.AddHours(12));
        Assert.Equal(A, after.OnCallChannelId);
        Assert.False(after.IsOverride);
        Assert.False(OnCallSchedule.Resolve(rotation, Start.AddHours(5)).IsOverride);
    }

    [Fact]
    public void Of_overlapping_overrides_the_latest_starting_wins()
    {
        var rotation = Rotation() with
        {
            Overrides = [new OnCallOverride(Start, Start.AddHours(10), B), new OnCallOverride(Start.AddHours(4), Start.AddHours(6), C)],
        };
        Assert.Equal(C, OnCallSchedule.Resolve(rotation, Start.AddHours(5)).OnCallChannelId);
        Assert.Equal(B, OnCallSchedule.Resolve(rotation, Start.AddHours(7)).OnCallChannelId);
    }

    [Fact]
    public void Escalation_targets_follow_the_override()
    {
        var rotation = Rotation() with { Overrides = [new OnCallOverride(Start, Start.AddHours(1), C)] };
        Assert.Equal([C], OnCallSchedule.EscalationTargets([], rotation, Start));
    }

    [Fact]
    public void Override_validation()
    {
        var ok = new OnCallRotationRequest { Name = "p", ChannelIds = [A], ShiftHours = 24, StartsAt = Start };
        Assert.Null((ok with { Overrides = [new OnCallOverride(Start, Start.AddHours(1), B)] }).Validate());
        Assert.NotNull((ok with { Overrides = [new OnCallOverride(Start, Start, B)] }).Validate());
        Assert.NotNull((ok with { Overrides = [new OnCallOverride(Start, Start.AddHours(1), Guid.Empty)] }).Validate());
        Assert.NotNull((ok with { Overrides = [.. Enumerable.Repeat(new OnCallOverride(Start, Start.AddHours(1), B), OnCallRotationRequest.MaxOverrides + 1)] }).Validate());
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

    // 2026-10-05 is a Monday. UTC zone keeps these independent of the host's time zone data.
    private static readonly OnCallCoverage Weekdays9To5 = new("UTC", [1, 2, 3, 4, 5], 9 * 60, 17 * 60);

    [Theory]
    [InlineData(5, 9, 0, true)]    // Monday 09:00, window opens
    [InlineData(5, 16, 59, true)]
    [InlineData(5, 17, 0, false)]  // end is exclusive
    [InlineData(5, 8, 59, false)]
    [InlineData(10, 12, 0, false)] // Saturday
    public void Coverage_window_by_weekday_and_time(int day, int hour, int minute, bool expected) =>
        Assert.Equal(expected, OnCallSchedule.IsCovered(Weekdays9To5, new DateTimeOffset(2026, 10, day, hour, minute, 0, TimeSpan.Zero)));

    [Fact]
    public void Overnight_window_belongs_to_the_day_it_starts()
    {
        var nights = new OnCallCoverage("UTC", [5], 22 * 60, 6 * 60); // Friday 22:00 -> Saturday 06:00
        Assert.True(OnCallSchedule.IsCovered(nights, new DateTimeOffset(2026, 10, 9, 23, 0, 0, TimeSpan.Zero)));
        Assert.True(OnCallSchedule.IsCovered(nights, new DateTimeOffset(2026, 10, 10, 5, 59, 0, TimeSpan.Zero)));
        Assert.False(OnCallSchedule.IsCovered(nights, new DateTimeOffset(2026, 10, 10, 6, 0, 0, TimeSpan.Zero)));
        Assert.False(OnCallSchedule.IsCovered(nights, new DateTimeOffset(2026, 10, 9, 5, 0, 0, TimeSpan.Zero))); // Friday early morning belongs to Thursday's window
    }

    [Fact]
    public void Coverage_is_read_in_its_time_zone()
    {
        Assert.True(OnCallSchedule.TryFindZone("Asia/Tokyo", out _));
        var tokyo = Weekdays9To5 with { TimeZone = "Asia/Tokyo" };
        // 00:30 UTC Monday is 09:30 in Tokyo.
        Assert.True(OnCallSchedule.IsCovered(tokyo, new DateTimeOffset(2026, 10, 5, 0, 30, 0, TimeSpan.Zero)));
        Assert.False(OnCallSchedule.IsCovered(Weekdays9To5, new DateTimeOffset(2026, 10, 5, 0, 30, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void Unknown_zone_counts_as_covered() =>
        Assert.True(OnCallSchedule.IsCovered(Weekdays9To5 with { TimeZone = "Not/AZone" }, Start));

    [Fact]
    public void Outside_coverage_nobody_is_paged_but_fixed_channels_remain()
    {
        var rotation = Rotation() with { Coverage = Weekdays9To5 };
        var saturday = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        Assert.False(OnCallSchedule.Resolve(rotation, saturday).InCoverage);
        Assert.Equal([B], OnCallSchedule.EscalationTargets([B], rotation, saturday));
        Assert.Equal([B, A], OnCallSchedule.EscalationTargets([B], rotation, Start));
    }

    [Fact]
    public void Override_pages_even_outside_coverage()
    {
        var saturday = new DateTimeOffset(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        var rotation = Rotation() with { Coverage = Weekdays9To5, Overrides = [new OnCallOverride(saturday.AddHours(-1), saturday.AddHours(1), C)] };
        var status = OnCallSchedule.Resolve(rotation, saturday);
        Assert.True(status.InCoverage);
        Assert.Equal(C, status.OnCallChannelId);
    }

    [Fact]
    public void Coverage_validation()
    {
        var ok = new OnCallRotationRequest { Name = "p", ChannelIds = [A], ShiftHours = 24, StartsAt = Start, Coverage = Weekdays9To5 };
        Assert.Null(ok.Validate());
        Assert.NotNull((ok with { Coverage = Weekdays9To5 with { TimeZone = "Not/AZone" } }).Validate());
        Assert.NotNull((ok with { Coverage = Weekdays9To5 with { Days = [] } }).Validate());
        Assert.NotNull((ok with { Coverage = Weekdays9To5 with { Days = [7] } }).Validate());
        Assert.NotNull((ok with { Coverage = Weekdays9To5 with { EndMinute = 9 * 60 } }).Validate());
        Assert.Null((ok with { Coverage = Weekdays9To5 with { StartMinute = 0, EndMinute = 1440 } }).Validate());
    }
}
