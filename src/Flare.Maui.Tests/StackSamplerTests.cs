using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace Flare.Maui.Tests;

public class StackSamplerTests
{
    private sealed class ManualTime : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    // The timer never fires within a test (1 h); samples are taken by calling Sample() directly.
    private static StackSampler Sampler(Func<string?> capture) => new(capture, TimeSpan.FromHours(1));

    [Fact]
    public void Fold_reverses_leaf_first_frames_into_a_root_first_line()
    {
        var folded = StackSampler.Fold("   at leaf\n   at mid\n   at root");
        Assert.Equal("root;mid;leaf", folded);
        Assert.Null(StackSampler.Fold("  "));
        Assert.Null(StackSampler.Fold(null));
    }

    [Fact]
    public void Samples_are_counted_per_distinct_stack_most_frequent_first()
    {
        var stacks = new Queue<string>(new[] { "at a\nat main", "at b\nat main", "at b\nat main" });
        var sampler = Sampler(() => stacks.Dequeue());
        sampler.Start();
        for (var i = 0; i < 3; i++) sampler.Sample();

        var result = sampler.Stop(keep: true);
        Assert.NotNull(result);
        Assert.Equal(3, result!.Value.Samples);
        Assert.Equal("2 main;b\n1 main;a", result.Value.Stacks);
    }

    [Fact]
    public void Stop_without_keep_or_without_samples_returns_null_and_a_failing_capture_is_ignored()
    {
        var sampler = Sampler(() => throw new InvalidOperationException());
        sampler.Start();
        sampler.Sample();
        Assert.Null(sampler.Stop(keep: true));

        var ok = Sampler(() => "at x");
        ok.Start();
        ok.Sample();
        Assert.Null(ok.Stop(keep: false));
    }

    [Fact]
    public void Output_is_capped_in_distinct_stacks_and_characters()
    {
        var n = 0;
        var sampler = Sampler(() => $"at frame{n++}");
        sampler.Start();
        for (var i = 0; i < 200; i++) sampler.Sample();
        var result = sampler.Stop(keep: true)!.Value;
        Assert.Equal(200, result.Samples);
        Assert.Equal(StackSampler.MaxStacks, result.Stacks.Split('\n').Length);

        var big = Sampler(() => "at " + new string('x', 3000) + n++);
        big.Start();
        for (var i = 0; i < 10; i++) big.Sample();
        Assert.True(big.Stop(keep: true)!.Value.Stacks.Length <= StackSampler.MaxChars);
    }

    [Fact]
    public void A_slow_screen_load_carries_the_profile_and_a_fast_one_does_not()
    {
        var spans = new List<Activity>();
        using var source = new ActivitySource("test.profile");
        using var provider = Sdk.CreateTracerProviderBuilder().AddSource("test.profile").AddInMemoryExporter(spans).Build();
        var time = new ManualTime();
        var sampler = Sampler(() => "at slow\nat main");
        var tracker = new PerformanceTracker(source, TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(700), time,
            sampler, TimeSpan.FromMilliseconds(500));

        tracker.BeginScreenLoad();
        sampler.Sample();
        time.Now += TimeSpan.FromMilliseconds(800);
        tracker.ScreenShown("//slow");

        tracker.BeginScreenLoad();
        sampler.Sample();
        time.Now += TimeSpan.FromMilliseconds(100);
        tracker.ScreenShown("//fast");

        var slow = spans.Single(s => s.GetTagItem("screen.name") as string == "//slow");
        Assert.Equal(1, slow.GetTagItem("profile.samples"));
        Assert.Equal("1 main;slow", slow.GetTagItem("profile.stacks"));
        var fast = spans.Single(s => s.GetTagItem("screen.name") as string == "//fast");
        Assert.Null(fast.GetTagItem("profile.stacks"));
    }

    [Fact]
    public void Options_validate_profile_settings()
    {
        FlareMauiOptions Opts(double threshold, double interval) => new()
        {
            Endpoint = new Uri("http://h:4318"), ServiceName = "a",
            ProfileSlowLoadThreshold = TimeSpan.FromMilliseconds(threshold),
            ProfileSampleInterval = TimeSpan.FromMilliseconds(interval),
        };
        Opts(500, 50).Validate();
        Assert.Throws<InvalidOperationException>(() => Opts(0, 50).Validate());
        Assert.Throws<InvalidOperationException>(() => Opts(500, 5).Validate());
    }
}
