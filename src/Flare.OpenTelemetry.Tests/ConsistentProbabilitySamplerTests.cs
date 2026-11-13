using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace Flare.OpenTelemetry.Tests;

public class ConsistentProbabilitySamplerTests
{
    [Theory]
    [InlineData(1.0, "0")]
    [InlineData(0.5, "8")]
    [InlineData(0.25, "c")]
    [InlineData(0.1, "e6666666666668")]
    public void Threshold_is_encoded_as_trimmed_hex(double probability, string expected)
    {
        var sampler = new ConsistentProbabilitySampler(probability);

        Assert.Equal(expected, ConsistentProbabilitySampler.EncodeThreshold(sampler.Threshold));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void Rejects_an_out_of_range_probability(double probability)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConsistentProbabilitySampler(probability));
    }

    [Fact]
    public void Keeps_when_randomness_is_at_or_above_the_threshold()
    {
        var sampler = new ConsistentProbabilitySampler(0.5); // threshold 0x80000000000000

        var kept = sampler.ShouldSample(Params(TraceId(0x80_0000_0000_0000)));
        var dropped = sampler.ShouldSample(Params(TraceId(0x7f_ffff_ffff_ffff)));

        Assert.Equal(SamplingDecision.RecordAndSample, kept.Decision);
        Assert.Equal("ot=th:8", kept.TraceStateString);
        Assert.Equal(SamplingDecision.Drop, dropped.Decision);
    }

    [Fact]
    public void Zero_probability_drops_everything()
    {
        var result = new ConsistentProbabilitySampler(0).ShouldSample(Params(TraceId(0xff_ffff_ffff_ffff)));

        Assert.Equal(SamplingDecision.Drop, result.Decision);
    }

    [Fact]
    public void Prefers_explicit_rv_over_the_trace_id()
    {
        var sampler = new ConsistentProbabilitySampler(0.5);
        var parent = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.None, "ot=rv:ffffffffffffff");

        // The trace id's randomness would drop it; rv keeps it.
        var result = sampler.ShouldSample(new SamplingParameters(parent, TraceId(0), "op", ActivityKind.Internal));

        Assert.Equal(SamplingDecision.RecordAndSample, result.Decision);
        Assert.Equal("ot=th:8;rv:ffffffffffffff", result.TraceStateString);
    }

    [Theory]
    [InlineData(null, "ot=th:c")]
    [InlineData("vendor=x", "ot=th:c,vendor=x")]
    [InlineData("vendor=x, ot=th:8;rv:00000000000001", "ot=th:c;rv:00000000000001,vendor=x")]
    public void MergeTraceState_sets_th_and_keeps_other_entries(string? traceState, string expected)
    {
        Assert.Equal(expected, ConsistentProbabilitySampler.MergeTraceState(traceState, "ot=th:c"));
    }

    [Fact]
    public void Keeps_roughly_the_configured_fraction()
    {
        var sampler = new ConsistentProbabilitySampler(0.25);

        var kept = Enumerable.Range(0, 20_000)
            .Count(_ => sampler.ShouldSample(Params(ActivityTraceId.CreateRandom())).Decision == SamplingDecision.RecordAndSample);

        Assert.InRange(kept, 4_500, 5_500);
    }

    [Fact]
    public void Kept_traces_carry_th_on_root_and_child_spans_through_the_sdk()
    {
        var source = new ActivitySource("flare.tests.consistent-sampler." + Guid.NewGuid());
        var exported = new List<Activity>();
        using var provider = Sdk.CreateTracerProviderBuilder()
            .AddSource(source.Name)
            .SetSampler(FlareOpenTelemetryExtensions.CreateSampler(0.5)!)
            .AddInMemoryExporter(exported)
            .Build();

        for (var i = 0; i < 200; i++)
        {
            using var root = source.StartActivity("root");
            using var child = source.StartActivity("child");
        }

        provider.ForceFlush();

        Assert.NotEmpty(exported);
        Assert.All(exported, a => Assert.Equal("ot=th:8", a.TraceStateString));
        // Parent-based: a kept trace keeps both of its spans.
        Assert.All(exported.GroupBy(a => a.TraceId), g => Assert.Equal(2, g.Count()));
    }

    private static SamplingParameters Params(ActivityTraceId traceId) =>
        new(default, traceId, "op", ActivityKind.Internal);

    private static ActivityTraceId TraceId(ulong randomness)
    {
        Span<byte> bytes = stackalloc byte[16];
        bytes.Fill(0xab);
        for (var i = 15; i >= 9; i--)
        {
            bytes[i] = (byte)randomness;
            randomness >>= 8;
        }

        return ActivityTraceId.CreateFromBytes(bytes);
    }
}
