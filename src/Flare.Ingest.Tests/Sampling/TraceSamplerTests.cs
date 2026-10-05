using Xunit;
using Flare.Ingest.Model;
using Flare.Ingest.Sampling;

namespace Flare.Ingest.Tests.Sampling;

public class TraceSamplerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

    private static SpanRecord Span(string traceId, string service = "svc", int status = 1, ulong durationMs = 10, string? spanId = null) => new()
    {
        TraceId = traceId,
        SpanId = spanId ?? Guid.NewGuid().ToString("N")[..16],
        Kind = 1,
        StartTime = T0,
        EndTime = T0.AddMilliseconds(durationMs),
        IngestedAt = T0,
        DurationNano = durationMs * 1_000_000UL,
        StatusCode = status,
        ServiceName = service,
        ResourceAttributes = new Dictionary<string, string>(),
        ScopeAttributes = new Dictionary<string, string>(),
        SpanAttributes = new Dictionary<string, string>(),
        Events = [],
        Links = [],
    };

    private static TraceSampler Sampler(int keepOneIn, Action<TraceSamplingOptions>? configure = null)
    {
        var o = new TraceSamplingOptions { Enabled = true, DefaultKeepOneIn = keepOneIn };
        configure?.Invoke(o);
        return new TraceSampler(o);
    }

    private static string TraceIdThat(Func<ulong, bool> predicate)
    {
        for (var i = 0; ; i++)
        {
            var id = i.ToString("x32");
            if (predicate(TraceSampler.HashTraceId(id)))
            {
                return id;
            }
        }
    }

    [Fact]
    public void KeepOneIn1_PassesEverythingThroughImmediately_AtWeight1()
    {
        var sampler = Sampler(1);
        var emit = new List<SpanRecord>();

        sampler.Add(Span("a"), null, T0, emit);

        var kept = Assert.Single(emit);
        Assert.Equal(1u, kept.SampleWeight);
        Assert.Equal(0, sampler.HeldSpans);
    }

    [Fact]
    public void HealthyTrace_IsHeldThenDecidedByHash_WithWeightN()
    {
        var sampler = Sampler(10);
        var keepId = TraceIdThat(h => h % 10 == 0);
        var dropId = TraceIdThat(h => h % 10 != 0);
        var emit = new List<SpanRecord>();

        sampler.Add(Span(keepId), null, T0, emit);
        sampler.Add(Span(dropId), null, T0, emit);
        Assert.Empty(emit);
        Assert.Equal(2, sampler.HeldSpans);

        sampler.Sweep(T0.AddSeconds(31), emit);

        var kept = Assert.Single(emit);
        Assert.Equal(keepId, kept.TraceId);
        Assert.Equal(10u, kept.SampleWeight);
        Assert.Equal(1, sampler.Dropped);
        Assert.Equal(0, sampler.HeldSpans);
    }

    [Fact]
    public void ErrorSpan_RescuesEarlierHeldSpans_AtWeight1()
    {
        var sampler = Sampler(1000);
        var id = TraceIdThat(h => h % 1000 != 0);
        var emit = new List<SpanRecord>();

        sampler.Add(Span(id, spanId: "child1"), null, T0, emit);
        sampler.Add(Span(id, spanId: "child2"), null, T0, emit);
        Assert.Empty(emit);

        sampler.Add(Span(id, status: 2, spanId: "root"), null, T0.AddSeconds(1), emit);

        Assert.Equal(3, emit.Count);
        Assert.All(emit, s => Assert.Equal(1u, s.SampleWeight));
        Assert.Equal(0, sampler.HeldSpans);

        // Later spans of the same trace follow the verdict and are kept at weight 1.
        sampler.Add(Span(id, spanId: "late"), null, T0.AddSeconds(2), emit);
        Assert.Equal(4, emit.Count);
        Assert.Equal(1u, emit[^1].SampleWeight);
    }

    [Fact]
    public void SlowSpan_MarksTraceKept_AndPerRuleThresholdOverridesDefault()
    {
        var sampler = Sampler(1000, o =>
        {
            o.SlowThreshold = TimeSpan.FromSeconds(1);
            o.Rules.Add(new TraceSamplingRule { Service = "batch", KeepOneIn = 1000, SlowThreshold = TimeSpan.FromSeconds(60) });
        });
        var id = TraceIdThat(h => h % 1000 != 0);
        var emit = new List<SpanRecord>();

        sampler.Add(Span(id, "batch", durationMs: 5_000), null, T0, emit); // under batch's 60s threshold
        Assert.Empty(emit);

        sampler.Add(Span(id, "web", durationMs: 1_500), null, T0, emit); // over the 1s default
        Assert.Equal(2, emit.Count);
        Assert.All(emit, s => Assert.Equal(1u, s.SampleWeight));
    }

    [Fact]
    public void LateErrorSpan_AfterTraceWasDroppedAtDecision_IsStillKept()
    {
        var sampler = Sampler(1000);
        var id = TraceIdThat(h => h % 1000 != 0);
        var emit = new List<SpanRecord>();

        sampler.Add(Span(id), null, T0, emit);
        sampler.Sweep(T0.AddSeconds(31), emit);
        Assert.Empty(emit);

        sampler.Add(Span(id, status: 2), null, T0.AddSeconds(32), emit);

        var kept = Assert.Single(emit);
        Assert.Equal(1u, kept.SampleWeight);
    }

    [Fact]
    public void HealthyLateSpan_FollowsItsOwnHash_AfterTheWindow()
    {
        var sampler = Sampler(10);
        var keepId = TraceIdThat(h => h % 10 == 0);
        var emit = new List<SpanRecord>();

        sampler.Add(Span(keepId), null, T0, emit);
        sampler.Sweep(T0.AddSeconds(31), emit);
        emit.Clear();

        sampler.Add(Span(keepId), null, T0.AddSeconds(40), emit);

        Assert.Equal(10u, Assert.Single(emit).SampleWeight);
    }

    [Fact]
    public void VerdictIsForgotten_AfterTtl_SoALaterSpanStartsANewHold()
    {
        var sampler = Sampler(1000, o => o.DecisionTtl = TimeSpan.FromMinutes(1));
        var id = TraceIdThat(h => h % 1000 != 0);
        var emit = new List<SpanRecord>();

        sampler.Add(Span(id, status: 2), null, T0, emit);
        sampler.Sweep(T0.AddMinutes(2), emit);
        emit.Clear();

        sampler.Add(Span(id), null, T0.AddMinutes(3), emit);

        Assert.Empty(emit);
        Assert.Equal(1, sampler.HeldSpans);
    }

    [Fact]
    public void PerServiceRule_ChangesTheRate_AndKeyRuleLosesToServiceKeyRule()
    {
        var keyA = Guid.NewGuid();
        var sampler = Sampler(100, o =>
        {
            o.Rules.Add(new TraceSamplingRule { Service = "checkout", KeepOneIn = 1 });
            o.Rules.Add(new TraceSamplingRule { IngestKeyId = keyA, KeepOneIn = 5 });
            o.Rules.Add(new TraceSamplingRule { Service = "web", IngestKeyId = keyA, KeepOneIn = 2 });
        });

        Assert.Equal(1, sampler.Resolve("checkout", null).KeepOneIn);
        Assert.Equal(100, sampler.Resolve("web", null).KeepOneIn);
        Assert.Equal(5, sampler.Resolve("api", keyA).KeepOneIn);
        Assert.Equal(2, sampler.Resolve("web", keyA).KeepOneIn);
        Assert.Equal(1, sampler.Resolve("checkout", keyA).KeepOneIn); // service-only beats key-only
    }

    [Fact]
    public void HoldCap_SkipsTheHold_AndDecidesByHashImmediately()
    {
        var sampler = Sampler(10, o => o.MaxHeldSpans = 1);
        var emit = new List<SpanRecord>();
        var first = TraceIdThat(h => h % 10 != 0);
        var keepId = TraceIdThat(h => h % 10 == 0);

        sampler.Add(Span(first), null, T0, emit);
        sampler.Add(Span(keepId), null, T0, emit);

        Assert.Equal(1, sampler.Overflowed);
        var kept = Assert.Single(emit);
        Assert.Equal(10u, kept.SampleWeight);
    }

    [Fact]
    public void FlushAll_DecidesEverythingHeld()
    {
        var sampler = Sampler(10);
        var keepId = TraceIdThat(h => h % 10 == 0);
        var emit = new List<SpanRecord>();
        sampler.Add(Span(keepId), null, T0, emit);

        sampler.FlushAll(emit);

        Assert.Equal(10u, Assert.Single(emit).SampleWeight);
        Assert.Equal(0, sampler.HeldSpans);
    }

    [Fact]
    public void HashDecision_AboutOneInNOfTraces_AreKept()
    {
        var sampler = Sampler(10);
        var emit = new List<SpanRecord>();
        for (var i = 0; i < 20_000; i++)
        {
            sampler.Add(Span(i.ToString("x32")), null, T0, emit);
        }
        sampler.Sweep(T0.AddSeconds(31), emit);

        Assert.InRange(emit.Count, 1_700, 2_300);
        Assert.Equal(20_000, emit.Sum(s => (long)s.SampleWeight), 4_000.0); // unbiased within ~20%
    }

    [Fact]
    public void Validate_RejectsBadValues()
    {
        Assert.Throws<InvalidOperationException>(() => new TraceSamplingOptions { DefaultKeepOneIn = 0 }.Validate());
        Assert.Throws<InvalidOperationException>(() => new TraceSamplingOptions { Rules = [new TraceSamplingRule { KeepOneIn = 5 }] }.Validate());
        Assert.Throws<InvalidOperationException>(() => new TraceSamplingOptions { Rules = [new TraceSamplingRule { Service = "x", KeepOneIn = 0 }] }.Validate());
    }
}
