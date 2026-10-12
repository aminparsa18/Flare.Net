using Xunit;
using Flare.Api.Model;
using Flare.Api.Status;

namespace Flare.Api.Tests.Status;

public sealed class StatusEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    private static SyntheticMonitor Monitor(bool enabled = true, int intervalSeconds = 60) => new()
    {
        Id = Guid.NewGuid(),
        Name = "api",
        Target = "https://example.com",
        Enabled = enabled,
        IntervalSeconds = intervalSeconds,
        CreatedAt = Now,
        UpdatedAt = Now,
    };

    private static SyntheticLocationStatus Location(string name, bool up, TimeSpan age) =>
        new(name, new SyntheticMonitorStatus(up, Now - age, 100, 200, null));

    [Fact]
    public void Monitor_with_every_location_up_is_operational()
    {
        var state = StatusEvaluator.MonitorState(Monitor(), [Location("a", true, TimeSpan.FromSeconds(10)), Location("b", true, TimeSpan.FromSeconds(10))], Now);
        Assert.Equal(StatusState.Operational, state);
    }

    [Fact]
    public void Monitor_with_some_locations_down_is_degraded()
    {
        var state = StatusEvaluator.MonitorState(Monitor(), [Location("a", true, TimeSpan.FromSeconds(10)), Location("b", false, TimeSpan.FromSeconds(10))], Now);
        Assert.Equal(StatusState.Degraded, state);
    }

    [Fact]
    public void Monitor_with_every_location_down_is_an_outage()
    {
        var state = StatusEvaluator.MonitorState(Monitor(), [Location("a", false, TimeSpan.FromSeconds(10))], Now);
        Assert.Equal(StatusState.Outage, state);
    }

    [Fact]
    public void Stale_results_are_ignored_so_a_stopped_monitor_does_not_stay_green()
    {
        var state = StatusEvaluator.MonitorState(Monitor(intervalSeconds: 60), [Location("a", true, TimeSpan.FromMinutes(30))], Now);
        Assert.Equal(StatusState.Unknown, state);
    }

    [Fact]
    public void Only_fresh_locations_count()
    {
        var state = StatusEvaluator.MonitorState(Monitor(), [Location("a", true, TimeSpan.FromSeconds(10)), Location("old", false, TimeSpan.FromHours(3))], Now);
        Assert.Equal(StatusState.Operational, state);
    }

    [Fact]
    public void Disabled_or_unprobed_monitor_is_unknown()
    {
        Assert.Equal(StatusState.Unknown, StatusEvaluator.MonitorState(Monitor(enabled: false), [Location("a", true, TimeSpan.FromSeconds(10))], Now));
        Assert.Equal(StatusState.Unknown, StatusEvaluator.MonitorState(Monitor(), [], Now));
    }

    [Theory]
    [InlineData(null, StatusState.Unknown)]
    [InlineData(1.0, StatusState.Operational)]
    [InlineData(0.0, StatusState.Operational)]
    [InlineData(-0.2, StatusState.Degraded)]
    public void Slo_state_follows_the_error_budget(double? remaining, StatusState expected) =>
        Assert.Equal(expected, StatusEvaluator.SloState(remaining));

    [Fact]
    public void Overall_is_the_worst_known_state()
    {
        Assert.Equal(StatusState.Outage, StatusEvaluator.Overall([StatusState.Operational, StatusState.Outage, StatusState.Degraded]));
        Assert.Equal(StatusState.Degraded, StatusEvaluator.Overall([StatusState.Operational, StatusState.Degraded, StatusState.Unknown]));
        Assert.Equal(StatusState.Operational, StatusEvaluator.Overall([StatusState.Operational, StatusState.Unknown]));
        Assert.Equal(StatusState.Unknown, StatusEvaluator.Overall([StatusState.Unknown]));
        Assert.Equal(StatusState.Unknown, StatusEvaluator.Overall([]));
    }

    [Fact]
    public void Days_covers_the_requested_span_oldest_first_with_gaps_as_null()
    {
        var today = new DateOnly(2026, 10, 9);
        var days = StatusEvaluator.Days(new Dictionary<DateOnly, double> { [today] = 99.12345, [today.AddDays(-2)] = 100 }, today, count: 3);

        Assert.Equal(["2026-10-07", "2026-10-08", "2026-10-09"], days.Select(d => d.Date));
        Assert.Equal([100d, null, 99.123], days.Select(d => d.UptimePercent));
    }

    [Fact]
    public void Uptime_percent_averages_only_recorded_days()
    {
        Assert.Equal(99.5, StatusEvaluator.UptimePercent([new("2026-10-07", 100), new("2026-10-08", null), new("2026-10-09", 99)]));
        Assert.Null(StatusEvaluator.UptimePercent([new("2026-10-09", null)]));
    }

    [Theory]
    [InlineData("status", true)]
    [InlineData("acme-prod-2", true)]
    [InlineData("-bad", false)]
    [InlineData("bad-", false)]
    [InlineData("Upper", false)]
    [InlineData("a/b", false)]
    [InlineData("", false)]
    public void Slug_validation(string slug, bool valid) => Assert.Equal(valid, StatusPageRequest.IsValidSlug(slug));

    [Fact]
    public void Request_rejects_a_component_without_a_target_or_name()
    {
        var noId = new StatusPageRequest { Slug = "s", Title = "T", Components = [new("API", StatusComponentKind.Monitor, Guid.Empty)] };
        var noName = new StatusPageRequest { Slug = "s", Title = "T", Components = [new(" ", StatusComponentKind.Slo, Guid.NewGuid())] };
        var ok = new StatusPageRequest { Slug = "s", Title = "T", Components = [new("API", StatusComponentKind.Slo, Guid.NewGuid())] };

        Assert.NotNull(noId.Validate());
        Assert.NotNull(noName.Validate());
        Assert.Null(ok.Validate());
    }
}
