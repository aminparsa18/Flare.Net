using System.Net;
using Xunit;

namespace Flare.Maui.Tests;

public class ReplayTests
{
    /// <summary>A clock whose timers fire only when the test says so, and which counts live timers.</summary>
    private sealed class ManualClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 12, 9, 0, 0, TimeSpan.Zero);
        public List<ManualTimer> Timers { get; } = [];
        public override DateTimeOffset GetUtcNow() => Now;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(callback, state, dueTime, period);
            Timers.Add(timer);
            return timer;
        }
    }

    private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period) : ITimer
    {
        public TimeSpan Due { get; } = due;
        public TimeSpan Period { get; } = period;
        public bool Disposed { get; private set; }
        public void Fire() { if (!Disposed) callback(state); }
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;
        public void Dispose() => Disposed = true;
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private static FlareMauiOptions Options(TimeSpan? interval = null) => new()
    {
        Endpoint = new Uri("http://h:4318/"), ServiceName = "shop", ScreenshotInterval = interval ?? TimeSpan.FromSeconds(10),
    };

    [Fact]
    public void Scheduler_ticks_on_the_interval_only_while_started()
    {
        var clock = new ManualClock();
        var ticks = 0;
        using var scheduler = new ReplayScheduler(TimeSpan.FromSeconds(10), () => ticks++, clock);

        scheduler.Start();
        scheduler.Start();                        // idempotent: still one timer
        var timer = Assert.Single(clock.Timers);
        Assert.Equal((TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10)), (timer.Due, timer.Period));
        timer.Fire();
        scheduler.Stop();
        timer.Fire();                             // a stopped timer does not tick

        Assert.Equal(1, ticks);
        Assert.False(scheduler.Running);

        scheduler.Start();                        // resume after background: a fresh timer
        Assert.Equal(2, clock.Timers.Count);
        Assert.True(scheduler.Running);
    }

    [Fact]
    public void Scheduler_survives_a_throwing_tick_and_stays_stopped_after_dispose()
    {
        var clock = new ManualClock();
        var scheduler = new ReplayScheduler(TimeSpan.FromSeconds(5), () => throw new InvalidOperationException("boom"), clock);
        scheduler.Start();

        clock.Timers[0].Fire();                   // does not throw

        scheduler.Dispose();
        scheduler.Start();
        Assert.False(scheduler.Running);
    }

    [Fact]
    public async Task Frame_upload_is_periodic_and_carries_no_trace_or_span()
    {
        var handler = new RecordingHandler();
        var reporter = new ScreenshotReporter(Options(), "sess1234", _ => Task.FromResult<byte[]?>(new byte[50]), new HttpClient(handler), periodic: true);

        await reporter.CaptureAndUploadAsync(string.Empty, string.Empty);

        var uri = Assert.Single(handler.Requests);
        Assert.Equal("http://h:4318/v1/screenshots?service=shop&session_id=sess1234&kind=periodic", uri.AbsoluteUri);
    }

    [Fact]
    public void Frames_are_capped_per_launch_and_not_dropped_for_a_slightly_late_tick()
    {
        var clock = new ManualClock();
        var uploads = 0;
        var reporter = new ScreenshotReporter(Options(), "sess1234", _ => { uploads++; return Task.FromResult<byte[]?>(null); },
            new HttpClient(new RecordingHandler()), clock, periodic: true, maxPerSession: 3, minInterval: TimeSpan.FromSeconds(5));

        for (var i = 0; i < 6; i++)
        {
            reporter.ReportFrame();
            Thread.Sleep(20);                     // let the fire-and-forget capture run
            clock.Now += TimeSpan.FromSeconds(9); // a tick slightly early still passes the half-interval spacing
        }

        Assert.Equal(3, uploads);
    }

    [Fact]
    public void Error_reporter_limits_are_unchanged_by_the_periodic_options()
    {
        var clock = new ManualClock();
        var uploads = 0;
        var reporter = new ScreenshotReporter(Options(), "sess1234", _ => { uploads++; return Task.FromResult<byte[]?>(null); },
            new HttpClient(new RecordingHandler()), clock);

        reporter.Report("a", "b", TimeSpan.FromSeconds(2));
        reporter.Report("a", "b", TimeSpan.FromSeconds(2)); // inside the 10 s error spacing

        Assert.Equal(1, uploads);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(0)]
    public void Options_reject_an_interval_under_five_seconds(int seconds) =>
        Assert.Throws<InvalidOperationException>(() => Options(TimeSpan.FromSeconds(seconds)).Validate());

    [Fact]
    public void Options_accept_five_seconds_and_default_to_masking_inputs()
    {
        var options = Options(TimeSpan.FromSeconds(5));
        options.Validate();

        Assert.True(options.MaskTextInputs);
        Assert.Null(new FlareMauiOptions().ScreenshotInterval);
    }

    [Fact]
    public void Options_reject_a_zero_frame_cap_and_check_the_size_limit_for_replay_alone()
    {
        var capped = Options(); capped.MaxReplayScreenshots = 0;
        var oversized = Options(); oversized.ScreenshotMaxBytes = 600 * 1024;

        Assert.Throws<InvalidOperationException>(capped.Validate);
        Assert.Throws<InvalidOperationException>(oversized.Validate);
    }
}
