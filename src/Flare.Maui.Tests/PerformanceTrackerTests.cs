using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace Flare.Maui.Tests;

public class PerformanceTrackerTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Rig : IDisposable
    {
        private readonly ActivitySource _source = new("test.perf");
        private readonly TracerProvider _provider;
        public List<Activity> Spans { get; } = new();
        public ManualTime Time { get; } = new();
        public PerformanceTracker Tracker { get; }

        public Rig()
        {
            _provider = Sdk.CreateTracerProviderBuilder().AddSource("test.perf").AddInMemoryExporter(Spans).Build();
            Tracker = new PerformanceTracker(_source, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(700), Time);
        }

        public void Dispose() { _provider.Dispose(); _source.Dispose(); }
    }

    [Fact]
    public void First_resume_is_a_cold_start_measured_from_process_start()
    {
        using var rig = new Rig();
        var processStart = rig.Time.Now - TimeSpan.FromMilliseconds(1800);

        rig.Tracker.NoteResumed(processStart, "process");

        var span = Assert.Single(rig.Spans);
        Assert.Equal("app.start", span.OperationName);
        Assert.Equal("cold", span.GetTagItem("app.start.type"));
        Assert.Equal("process", span.GetTagItem("app.start.origin"));
        Assert.Equal(1800, (long)span.Duration.TotalMilliseconds);
    }

    [Fact]
    public void Later_resume_is_warm_only_after_a_foreground_begin()
    {
        using var rig = new Rig();
        rig.Tracker.NoteResumed(rig.Time.Now, "sdk");
        rig.Spans.Clear();

        rig.Time.Now += TimeSpan.FromSeconds(30);
        rig.Tracker.NoteResumed(rig.Time.Now, "sdk");     // a dialog closing, not a start
        Assert.Empty(rig.Spans);

        rig.Tracker.NoteForegroundBegin();
        rig.Time.Now += TimeSpan.FromMilliseconds(400);
        rig.Tracker.NoteResumed(rig.Time.Now, "sdk");

        var span = Assert.Single(rig.Spans);
        Assert.Equal("warm", span.GetTagItem("app.start.type"));
        Assert.Equal(400, (long)span.Duration.TotalMilliseconds);

        rig.Tracker.NoteResumed(rig.Time.Now, "sdk");     // consumed: no second warm start
        Assert.Single(rig.Spans);
    }

    [Fact]
    public void Screen_load_spans_navigation_start_to_shown_and_needs_a_begin()
    {
        using var rig = new Rig();

        rig.Tracker.ScreenShown("//home");                // no begin: nothing to time
        Assert.Empty(rig.Spans);

        rig.Tracker.BeginScreenLoad();
        rig.Time.Now += TimeSpan.FromMilliseconds(250);
        rig.Tracker.ScreenShown("//cart");

        var span = Assert.Single(rig.Spans);
        Assert.Equal("screen.load", span.OperationName);
        Assert.Equal("//cart", span.GetTagItem("screen.name"));
        Assert.Equal(250, (long)span.Duration.TotalMilliseconds);

        rig.Tracker.ScreenShown("//cart");                // the begin was consumed
        Assert.Single(rig.Spans);
    }

    [Fact]
    public void Frames_are_counted_per_screen_and_frozen_also_counts_as_slow()
    {
        using var rig = new Rig();
        rig.Tracker.ScreenShown("//feed");
        foreach (var ms in new[] { 8, 16, 19, 20, 45, 700, 1200 })
        {
            rig.Time.Now += TimeSpan.FromMilliseconds(ms);
            rig.Tracker.RecordFrame(TimeSpan.FromMilliseconds(ms));
        }

        rig.Tracker.ScreenShown("//detail");              // closes the //feed window

        var span = Assert.Single(rig.Spans, s => s.OperationName == "screen.frames");
        Assert.Equal("//feed", span.GetTagItem("screen.name"));
        Assert.Equal(7L, span.GetTagItem("frames.total"));
        Assert.Equal(4L, span.GetTagItem("frames.slow"));  // 20, 45, 700, 1200
        Assert.Equal(2L, span.GetTagItem("frames.frozen")); // 700, 1200
    }

    [Fact]
    public void Flush_without_frames_reports_nothing_and_resets_the_window()
    {
        using var rig = new Rig();
        rig.Tracker.FlushFrames();
        Assert.Empty(rig.Spans);

        rig.Tracker.ScreenShown("//a");
        rig.Tracker.RecordFrame(TimeSpan.FromMilliseconds(30));
        rig.Tracker.FlushFrames();
        rig.Tracker.FlushFrames();
        Assert.Single(rig.Spans);
        rig.Tracker.RecordFrame(TimeSpan.Zero);           // ignored
        rig.Tracker.FlushFrames();
        Assert.Single(rig.Spans);
    }

    [Fact]
    public void Options_validate_frame_thresholds()
    {
        FlareMauiOptions Opts(double slow, double frozen) => new()
        {
            Endpoint = new Uri("http://h:4318"), ServiceName = "a",
            SlowFrameThreshold = TimeSpan.FromMilliseconds(slow), FrozenFrameThreshold = TimeSpan.FromMilliseconds(frozen),
        };
        Opts(20, 700).Validate();
        Assert.Throws<InvalidOperationException>(() => Opts(0, 700).Validate());
        Assert.Throws<InvalidOperationException>(() => Opts(700, 700).Validate());
    }
}
