using Flare.Api.Tempo;
using Xunit;

namespace Flare.Api.Tests.Tempo;

public class TempoMetricsWindowTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_790_000_100);

    private static TempoMetricsWindow Range(string? start = null, string? end = null, string? since = null, string? step = null) =>
        TempoMetricsWindow.Resolve(start, end, since, step, Now, instant: false, maxSteps: 1500);

    [Fact]
    public void Defaults_AreTheLastHourInTwoHundredSteps()
    {
        var window = Range();

        // 3600s / 200 = 18s; from rounds down and to rounds up to a multiple of it.
        Assert.Equal(18, window.StepSeconds);
        Assert.Equal(0, window.FromSeconds % 18);
        Assert.Equal(0, window.ToSeconds % 18);
        Assert.True(window.FromSeconds <= 1_790_000_100 - 3600);
        Assert.True(window.ToSeconds >= 1_790_000_100);
        Assert.True(window.ToSeconds - 1_790_000_100 < 18);
    }

    [Theory]
    [InlineData("30s", 30)]
    [InlineData("1m", 60)]
    [InlineData("1m30s", 90)]
    [InlineData("15", 15)]
    [InlineData("1.2s", 2)]
    public void Step_IsADurationOrWholeSeconds(string step, int expected)
    {
        Assert.Equal(expected, Range(start: "1790000000", end: "1790000600", step: step).StepSeconds);
    }

    [Fact]
    public void Range_IsAlignedToTheStep()
    {
        var window = Range(start: "1790000007", end: "1790000095", step: "30s");

        Assert.Equal(1_789_999_980, window.FromSeconds);
        Assert.Equal(1_790_000_100, window.ToSeconds);
        Assert.Equal(4, window.Steps);
    }

    [Fact]
    public void Times_AreSecondsNanosecondsOrRfc3339()
    {
        var seconds = Range(start: "1790000000", end: "1790000600", step: "10");
        var nanos = Range(start: "1790000000000000000", end: "1790000600000000000", step: "10");
        var text = Range(start: "2026-09-21T14:13:20Z", end: "2026-09-21T14:23:20Z", step: "10");

        Assert.Equal(seconds, nanos);
        Assert.Equal(1_790_000_000, text.FromSeconds);
        Assert.Equal(60, text.Steps);
    }

    [Fact]
    public void Since_StandsInForStart()
    {
        var window = Range(end: "1790003640", since: "10m", step: "60");

        Assert.Equal(1_790_003_040, window.FromSeconds);
        Assert.Equal(1_790_003_640, window.ToSeconds);
    }

    [Fact]
    public void Instant_IsOneStepAsLongAsTheRange()
    {
        var window = TempoMetricsWindow.Resolve("1790000000", "1790000600", null, null, Now, instant: true, maxSteps: 1500);

        Assert.Equal(new TempoMetricsWindow(1_790_000_000, 1_790_000_600, 600), window);
        Assert.Equal(1, window.Steps);
    }

    [Theory]
    [InlineData("1790000600", "1790000000", null, "60", "after")]
    [InlineData("1790000000", "1790000600", null, "0.5s", "at least one second")]
    [InlineData("1790000000", "1790000600", null, "soon", "invalid step")]
    [InlineData("yesterday", "1790000600", null, "60", "invalid 'start'")]
    [InlineData(null, "1790000600", "-1h", "60", "invalid 'since'")]
    [InlineData("1790000000", "1790100000", null, "1s", "at most 1500")]
    public void Invalid_IsAnArgumentException(string? start, string end, string? since, string step, string mentions)
    {
        var ex = Assert.Throws<ArgumentException>(() => Range(start, end, since, step));

        Assert.Contains(mentions, ex.Message);
    }
}
