using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Xunit;

namespace Flare.Maui.Tests;

public class NativeCrashMatcherTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static RunRecord Run(string id, int startMin, int seenMin, bool fg = false, bool fatal = false) => new()
    {
        SessionId = id, StartedAt = T0.AddMinutes(startMin), LastSeenAt = T0.AddMinutes(seenMin),
        Version = "1.0", Build = "1", Foreground = fg, FatalReported = fatal,
    };

    private static NativeCrash Exact(string kind, int min) => new(kind, "r", T0.AddMinutes(min));

    [Fact]
    public void Exact_crash_goes_to_the_latest_run_started_before_it()
    {
        var runs = new[] { Run("a", 0, 5), Run("b", 10, 15), Run("c", 30, 31) };
        var result = NativeCrashMatcher.Match([Exact("Anr", 20)], runs, null);
        Assert.Equal("b", Assert.Single(result.ToReport).Run.SessionId);
    }

    [Fact]
    public void Crash_before_the_first_recorded_run_is_ignored_but_advances_the_watermark()
    {
        var result = NativeCrashMatcher.Match([Exact("NativeCrash", -5)], [Run("a", 0, 5)], null);
        Assert.Empty(result.ToReport);
        Assert.Equal(T0.AddMinutes(-5), result.Watermark);
    }

    [Fact]
    public void Already_handled_crashes_are_skipped_by_the_watermark()
    {
        var runs = new[] { Run("a", 0, 5) };
        var result = NativeCrashMatcher.Match([Exact("Anr", 3), Exact("Anr", 4)], runs, T0.AddMinutes(3));
        Assert.Equal(T0.AddMinutes(4), Assert.Single(result.ToReport).Crash.Timestamp);
        Assert.Equal(T0.AddMinutes(4), result.Watermark);
    }

    [Fact]
    public void Plain_crash_on_a_run_whose_fatal_exception_was_reported_is_not_reported_twice()
    {
        var run = Run("a", 0, 5, fatal: true);
        var result = NativeCrashMatcher.Match([Exact("NativeCrash", 5)], [run], null);
        Assert.Empty(result.ToReport);
        Assert.True(run.NativeReported);
    }

    [Fact]
    public void Anr_on_a_run_with_a_reported_fatal_is_still_reported()
    {
        var result = NativeCrashMatcher.Match([Exact("Anr", 5)], [Run("a", 0, 5, fatal: true)], null);
        Assert.Single(result.ToReport);
    }

    [Fact]
    public void Windowed_crash_goes_to_the_latest_unclean_run_in_the_window()
    {
        var runs = new[] { Run("old", 0, 1, fg: true), Run("clean", 100, 110), Run("crashed", 200, 210, fg: true), Run("newer", 300, 301, fg: true) };
        var crash = new NativeCrash("NativeCrash", "r", T0.AddMinutes(250), WindowStart: T0.AddMinutes(150));
        var result = NativeCrashMatcher.Match([crash], runs, null);
        Assert.Equal("crashed", Assert.Single(result.ToReport).Run.SessionId);
    }

    [Fact]
    public void Two_windowed_crashes_take_two_different_runs()
    {
        var runs = new[] { Run("one", 0, 10, fg: true), Run("two", 20, 30, fg: true) };
        var win = T0.AddMinutes(-1);
        var crashes = new[] { new NativeCrash("NativeCrash", "a", T0.AddMinutes(100), WindowStart: win), new NativeCrash("NativeCrash", "b", T0.AddMinutes(101), WindowStart: win) };
        var result = NativeCrashMatcher.Match(crashes, runs, null);
        Assert.Equal(["two", "one"], result.ToReport.Select(r => r.Run.SessionId));
    }

    [Fact]
    public void Windowed_crash_with_no_unclean_run_is_dropped()
    {
        var crash = new NativeCrash("NativeCrash", "r", T0.AddMinutes(50), WindowStart: T0);
        Assert.Empty(NativeCrashMatcher.Match([crash], [Run("a", 0, 5)], null).ToReport);
    }
}

public sealed class RunTrackerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "flare-runs-" + Guid.NewGuid().ToString("N"));
    private string Path_ => Path.Combine(_dir, "runs.json");
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void Second_launch_sees_the_first_with_its_foreground_state()
    {
        var first = new RunTracker(Path_);
        Assert.Empty(first.Begin("s1", "1.0", "7"));
        first.Touch(foreground: false);
        first.Touch(foreground: true);
        first.MarkFatal();

        var prior = Assert.Single(new RunTracker(Path_).Begin("s2", "1.1", "8"));
        Assert.Equal(("s1", "1.0", "7"), (prior.SessionId, prior.Version, prior.Build));
        Assert.True(prior.Foreground);
        Assert.True(prior.FatalReported);
    }

    [Fact]
    public void Only_the_last_runs_are_kept()
    {
        for (var i = 0; i < RunTracker.MaxRuns + 5; i++) new RunTracker(Path_).Begin("s" + i, "1.0", "1");
        var prior = new RunTracker(Path_).Begin("last", "1.0", "1");
        Assert.Equal(RunTracker.MaxRuns, prior.Count);
        Assert.Equal("s" + (RunTracker.MaxRuns + 4), prior[^1].SessionId);
    }

    [Fact]
    public void A_corrupt_file_starts_a_fresh_journal()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path_, "{not json");
        Assert.Empty(new RunTracker(Path_).Begin("s", "1.0", "1"));
    }

    [Fact]
    public void Claim_advances_the_watermark_so_a_repeat_is_not_reported()
    {
        new RunTracker(Path_).Begin("s1", "1.0", "1");
        Thread.Sleep(20);
        var crash = new NativeCrash("Anr", "r", DateTimeOffset.UtcNow);
        Thread.Sleep(20);
        var second = new RunTracker(Path_);
        second.Begin("s2", "1.0", "1");
        Assert.Single(second.Claim([crash]));

        var third = new RunTracker(Path_);
        third.Begin("s3", "1.0", "1");
        Assert.Empty(third.Claim([crash]));
    }
}

public class NativeCrashEmitterTests
{
    [Fact]
    public void Emits_an_escaped_unhandled_exception_span_in_the_earlier_session_and_version()
    {
        var exported = new List<Activity>();
        var emitter = new NativeCrashEmitter(
            run => ResourceBuilder.CreateEmpty().AddService("app", serviceVersion: run.Version),
            b => b.AddInMemoryExporter(exported));
        var at = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var run = new RunRecord { SessionId = "old-session", Version = "2.2.0", Build = "9", StartedAt = at.AddMinutes(-5), LastSeenAt = at };

        emitter.Emit([(new NativeCrash("Anr", "Input dispatching timed out", at, StackTrace: "main thread blocked"), run)]);

        var span = Assert.Single(exported);
        Assert.Equal("app.unhandled_exception", span.DisplayName);
        Assert.Equal("old-session", span.GetTagItem("session.id"));
        Assert.Equal(true, span.GetTagItem("exception.escaped"));
        Assert.Equal("Anr", span.GetTagItem("crash.kind"));
        Assert.Equal(at.UtcDateTime, span.StartTimeUtc);
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        var ev = Assert.Single(span.Events);
        Assert.Equal("Native.Anr", ev.Tags.First(t => t.Key == "exception.type").Value);
        Assert.Equal("main thread blocked", ev.Tags.First(t => t.Key == "exception.stacktrace").Value);
    }

    [Fact]
    public void Scrubber_applies_to_the_exception_event_and_a_throwing_scrubber_keeps_the_value()
    {
        var exported = new List<Activity>();
        var at = DateTimeOffset.UtcNow;
        var run = new RunRecord { SessionId = "s", Version = "1", StartedAt = at.AddMinutes(-1), LastSeenAt = at };
        var crash = new NativeCrash("Crash", "secret reason", at);

        new NativeCrashEmitter(_ => ResourceBuilder.CreateEmpty(), b => b.AddInMemoryExporter(exported),
            (k, v) => k == "exception.message" ? "[redacted]" : v).Emit([(crash, run)]);
        new NativeCrashEmitter(_ => ResourceBuilder.CreateEmpty(), b => b.AddInMemoryExporter(exported),
            (_, _) => throw new InvalidOperationException()).Emit([(crash, run)]);

        Assert.Equal("[redacted]", exported[0].Events.Single().Tags.First(t => t.Key == "exception.message").Value);
        Assert.Equal("secret reason", exported[1].Events.Single().Tags.First(t => t.Key == "exception.message").Value);
    }

    [Fact]
    public void Long_stacks_are_truncated()
    {
        var exported = new List<Activity>();
        var at = DateTimeOffset.UtcNow;
        var run = new RunRecord { SessionId = "s", Version = "1", StartedAt = at.AddMinutes(-1), LastSeenAt = at };
        new NativeCrashEmitter(_ => ResourceBuilder.CreateEmpty(), b => b.AddInMemoryExporter(exported))
            .Emit([(new NativeCrash("Anr", "r", at, StackTrace: new string('x', NativeCrashEmitter.StackLimit + 100)), run)]);
        var stack = (string)exported.Single().Events.Single().Tags.First(t => t.Key == "exception.stacktrace").Value!;
        Assert.Equal(NativeCrashEmitter.StackLimit, stack.Length);
    }
}
