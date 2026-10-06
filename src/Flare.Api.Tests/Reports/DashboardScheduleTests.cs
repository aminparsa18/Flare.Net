using Flare.Api.Model;
using Flare.Api.Reports;
using Xunit;

namespace Flare.Api.Tests.Reports;

public class DashboardScheduleTests
{
    private static DashboardScheduleRequest Request(Action<DashboardScheduleRequestBuilder>? tweak = null)
    {
        var b = new DashboardScheduleRequestBuilder();
        tweak?.Invoke(b);
        return new DashboardScheduleRequest
        {
            Name = b.Name,
            Cron = b.Cron,
            TimeZone = b.TimeZone,
            Recipients = b.Recipients,
            TimeRange = b.TimeRange,
            VariableQuery = b.VariableQuery,
            Format = b.Format,
        };
    }

    private sealed class DashboardScheduleRequestBuilder
    {
        public string Name = "Weekly SLO";
        public string Cron = "0 8 * * 1";
        public string TimeZone = "UTC";
        public string Recipients = "team@example.com";
        public string? TimeRange = "7d";
        public string? VariableQuery;
        public string? Format;
    }

    [Fact]
    public void A_normal_request_is_valid() => Assert.Null(Request().Validate());

    [Theory]
    [InlineData("")]
    [InlineData("* * *")]
    [InlineData("0 8 * * * *")]
    [InlineData("every monday")]
    [InlineData("61 8 * * 1")]
    public void A_bad_cron_is_rejected(string cron) =>
        Assert.Contains("cron", Request(b => b.Cron = cron).Validate());

    [Fact]
    public void An_unknown_time_zone_is_rejected() =>
        Assert.Contains("timeZone", Request(b => b.TimeZone = "Mars/Olympus").Validate());

    [Theory]
    [InlineData("")]
    [InlineData(" , ; ")]
    public void Recipients_are_required(string recipients) =>
        Assert.Contains("recipients", Request(b => b.Recipients = recipients).Validate());

    [Fact]
    public void A_malformed_address_is_rejected() =>
        Assert.Contains("valid email", Request(b => b.Recipients = "team@example.com, not-an-email").Validate());

    [Fact]
    public void Too_many_recipients_are_rejected()
    {
        var many = string.Join(",", Enumerable.Range(0, DashboardScheduleRequest.MaxRecipients + 1).Select(i => $"u{i}@example.com"));
        Assert.Contains("at most", Request(b => b.Recipients = many).Validate());
    }

    [Theory]
    [InlineData("7d", true)]
    [InlineData("", true)]
    [InlineData("custom", false)]
    [InlineData("off", false)]
    [InlineData("2w", false)]
    public void Only_fixed_range_presets_are_accepted(string range, bool valid) =>
        Assert.Equal(valid, Request(b => b.TimeRange = range).Validate() is null);

    [Theory]
    [InlineData("pdf", true)]
    [InlineData("png", true)]
    [InlineData("gif", false)]
    public void Format_is_pdf_or_png(string format, bool valid) =>
        Assert.Equal(valid, Request(b => b.Format = format).Validate() is null);

    [Fact]
    public void Next_occurrence_is_read_in_the_schedules_time_zone()
    {
        // Mondays 08:00 in New York (UTC-4 in October): from Sunday 2026-10-04 12:00 UTC, the next is Monday 12:00 UTC.
        var after = new DateTimeOffset(2026, 10, 4, 12, 0, 0, TimeSpan.Zero);
        var next = DashboardScheduleRequest.NextOccurrence("0 8 * * 1", "America/New_York", after);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 12, 0, 0, TimeSpan.Zero), next);
    }

    [Fact]
    public void Next_occurrence_is_strictly_after_the_given_instant()
    {
        var at = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero);
        var next = DashboardScheduleRequest.NextOccurrence("0 8 * * *", "UTC", at);
        Assert.Equal(at.AddDays(1), next);
    }

    [Fact]
    public void A_cron_that_never_fires_again_has_no_next_occurrence() =>
        Assert.Null(DashboardScheduleRequest.NextOccurrence("0 0 30 2 *", "UTC", new DateTimeOffset(2026, 10, 5, 0, 0, 0, TimeSpan.Zero)));

    [Fact]
    public void Next_occurrence_of_garbage_is_null()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Null(DashboardScheduleRequest.NextOccurrence("nope", "UTC", now));
        Assert.Null(DashboardScheduleRequest.NextOccurrence("0 8 * * *", "Mars/Olympus", now));
    }

    private static DashboardSchedule Schedule(string range = "", string variables = "") => new()
    {
        Id = Guid.NewGuid(),
        DashboardId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Name = "n",
        Cron = "0 8 * * 1",
        TimeZone = "UTC",
        Recipients = "a@example.com",
        TimeRange = range,
        VariableQuery = variables,
        NextRunAt = default,
        CreatedAt = default,
        UpdatedAt = default,
    };

    [Fact]
    public void The_render_url_carries_report_mode_range_and_variables()
    {
        var url = DashboardReportUrl.Build("https://flare.example.com/", Schedule("7d", "var-svc=api&var-svc=web"));
        Assert.Equal("https://flare.example.com/dashboards/11111111-2222-3333-4444-555555555555?report=1&range=7d&var-svc=api&var-svc=web", url);
    }

    [Fact]
    public void The_render_url_with_defaults_is_just_report_mode() =>
        Assert.EndsWith("?report=1", DashboardReportUrl.Build("https://flare.example.com", Schedule()));

    [Fact]
    public void The_view_link_drops_report_mode()
    {
        Assert.Equal("https://flare.example.com/dashboards/11111111-2222-3333-4444-555555555555", DashboardReportUrl.BuildViewLink("https://flare.example.com", Schedule()));
        Assert.Equal("https://flare.example.com/dashboards/11111111-2222-3333-4444-555555555555?range=7d", DashboardReportUrl.BuildViewLink("https://flare.example.com", Schedule("7d")));
    }
}
