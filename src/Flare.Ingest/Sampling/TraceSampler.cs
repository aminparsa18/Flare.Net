using Flare.Ingest.Model;

namespace Flare.Ingest.Sampling;

/// <summary>
/// The decision core of ingest-side sampling (ADR-0122): pure, synchronous and clock-injected
/// so it is unit-testable without hosting. Spans go in via <see cref="Add"/>; whatever should
/// be persisted right now comes out in the caller's <c>emit</c> list. <see cref="Sweep"/>
/// decides traces whose hold window has ended, <see cref="FlushAll"/> decides everything left.
/// </summary>
/// <remarks>
/// State is per process. Head decisions hash the trace id, so replicas agree on them; a
/// tail "keep" discovered on one replica does not reach another replica holding other spans
/// of the same trace (route a trace's spans to one replica, e.g. an otelcol loadbalancing
/// exporter, if that matters).
/// </remarks>
public sealed class TraceSampler
{
    private const int ShardCount = 16;

    private readonly TraceSamplingOptions _options;
    private readonly Shard[] _shards = new Shard[ShardCount];
    private long _held;
    private long _keptHead;
    private long _keptTail;
    private long _dropped;
    private long _overflow;

    public TraceSampler(TraceSamplingOptions options)
    {
        options.Validate();
        _options = options;
        for (var i = 0; i < ShardCount; i++)
        {
            _shards[i] = new Shard();
        }
    }

    /// <summary>Spans currently held waiting for their window to end.</summary>
    public long HeldSpans => Interlocked.Read(ref _held);

    /// <summary>Spans kept by the 1-in-N head hash.</summary>
    public long KeptByHash => Interlocked.Read(ref _keptHead);

    /// <summary>Spans kept because their trace had an error or slow span (or the policy keeps everything).</summary>
    public long KeptByTail => Interlocked.Read(ref _keptTail);

    public long Dropped => Interlocked.Read(ref _dropped);

    /// <summary>Spans that skipped the hold because <see cref="TraceSamplingOptions.MaxHeldSpans"/> was reached.</summary>
    public long Overflowed => Interlocked.Read(ref _overflow);

    public void Add(SpanRecord span, Guid? ingestKeyId, DateTimeOffset now, List<SpanRecord> emit)
    {
        var policy = Resolve(span.ServiceName, ingestKeyId);
        var signal = IsSignal(span, policy);

        if (policy.KeepOneIn == 1 && !signal)
        {
            Interlocked.Increment(ref _keptTail);
            emit.Add(span);
            return;
        }

        var shard = ShardFor(span.TraceId);
        var nowTicks = now.UtcTicks;
        lock (shard)
        {
            shard.Entries.TryGetValue(span.TraceId, out var entry);

            if (entry is null)
            {
                if (signal)
                {
                    entry = new TraceEntry(span.TraceId, TraceVerdict.Keep);
                    shard.Entries[span.TraceId] = entry;
                    shard.Expiry.Enqueue((nowTicks + _options.DecisionTtl.Ticks, entry));
                    EmitKept(span, 1, emit);
                    return;
                }
                if (Interlocked.Read(ref _held) >= _options.MaxHeldSpans)
                {
                    Interlocked.Increment(ref _overflow);
                    DecideByHash(span, policy.KeepOneIn, emit);
                    return;
                }
                entry = new TraceEntry(span.TraceId, TraceVerdict.Pending);
                shard.Entries[span.TraceId] = entry;
                shard.Holds.Enqueue((nowTicks + _options.HoldWindow.Ticks, entry));
                Hold(entry, span, policy.KeepOneIn);
                return;
            }

            switch (entry.Verdict)
            {
                case TraceVerdict.Keep:
                    EmitKept(span, 1, emit);
                    break;
                case TraceVerdict.Healthy:
                    if (signal)
                    {
                        entry.Verdict = TraceVerdict.Keep;
                        EmitKept(span, 1, emit);
                    }
                    else
                    {
                        DecideByHash(span, policy.KeepOneIn, emit);
                    }
                    break;
                default:
                    if (signal)
                    {
                        Promote(entry, emit);
                        EmitKept(span, 1, emit);
                    }
                    else if (Interlocked.Read(ref _held) >= _options.MaxHeldSpans)
                    {
                        Interlocked.Increment(ref _overflow);
                        DecideByHash(span, policy.KeepOneIn, emit);
                    }
                    else
                    {
                        Hold(entry, span, policy.KeepOneIn);
                    }
                    break;
            }
        }
    }

    /// <summary>Decides every trace whose hold window has ended and forgets verdicts past their TTL.</summary>
    public void Sweep(DateTimeOffset now, List<SpanRecord> emit)
    {
        var nowTicks = now.UtcTicks;
        foreach (var shard in _shards)
        {
            lock (shard)
            {
                while (shard.Holds.TryPeek(out var due) && due.DueTicks <= nowTicks)
                {
                    shard.Holds.Dequeue();
                    var entry = due.Entry;
                    if (entry.Verdict == TraceVerdict.Pending)
                    {
                        DecideHeld(entry, emit);
                        entry.Verdict = TraceVerdict.Healthy;
                    }
                    shard.Expiry.Enqueue((nowTicks + _options.DecisionTtl.Ticks, entry));
                }

                while (shard.Expiry.TryPeek(out var due) && due.DueTicks <= nowTicks)
                {
                    shard.Expiry.Dequeue();
                    if (shard.Entries.TryGetValue(due.Entry.TraceId, out var current) && ReferenceEquals(current, due.Entry))
                    {
                        shard.Entries.Remove(due.Entry.TraceId);
                    }
                }
            }
        }
    }

    /// <summary>Decides every held trace now (shutdown) and clears all state.</summary>
    public void FlushAll(List<SpanRecord> emit)
    {
        foreach (var shard in _shards)
        {
            lock (shard)
            {
                foreach (var entry in shard.Entries.Values)
                {
                    if (entry.Verdict == TraceVerdict.Pending)
                    {
                        DecideHeld(entry, emit);
                    }
                }
                shard.Entries.Clear();
                shard.Holds.Clear();
                shard.Expiry.Clear();
            }
        }
    }

    /// <summary>Stable across processes and runs (unlike <see cref="string.GetHashCode()"/>) so replicas make the same head decision.</summary>
    public static ulong HashTraceId(string traceId)
    {
        var h = 14695981039346656037UL;
        foreach (var c in traceId)
        {
            h = (h ^ c) * 1099511628211UL;
        }
        // murmur3 finalizer: FNV's low bits are weak for a modulo-N bucket choice.
        h ^= h >> 33;
        h *= 0xff51afd7ed558ccdUL;
        h ^= h >> 33;
        h *= 0xc4ceb9fe1a85ec53UL;
        h ^= h >> 33;
        return h;
    }

    public Policy Resolve(string? service, Guid? keyId)
    {
        Policy? serviceOnly = null;
        Policy? keyOnly = null;
        foreach (var rule in _options.Rules)
        {
            var serviceMatches = rule.Service is not null && string.Equals(rule.Service, service, StringComparison.Ordinal);
            var keyMatches = rule.IngestKeyId is not null && rule.IngestKeyId == keyId;
            var p = new Policy(rule.KeepOneIn, (rule.SlowThreshold ?? _options.SlowThreshold));

            if (rule.Service is not null && rule.IngestKeyId is not null)
            {
                if (serviceMatches && keyMatches)
                {
                    return p;
                }
            }
            else if (serviceMatches)
            {
                serviceOnly ??= p;
            }
            else if (keyMatches)
            {
                keyOnly ??= p;
            }
        }

        return serviceOnly ?? keyOnly ?? new Policy(_options.DefaultKeepOneIn, _options.SlowThreshold);
    }

    private static bool IsSignal(SpanRecord span, Policy policy) =>
        span.StatusCode == 2
        || (policy.SlowThreshold > TimeSpan.Zero && span.DurationNano >= (ulong)policy.SlowThreshold.Ticks * 100UL);

    private void Hold(TraceEntry entry, SpanRecord span, int keepOneIn)
    {
        entry.Held ??= [];
        entry.Held.Add((span, keepOneIn));
        Interlocked.Increment(ref _held);
    }

    private void Promote(TraceEntry entry, List<SpanRecord> emit)
    {
        if (entry.Held is { } held)
        {
            foreach (var (span, _) in held)
            {
                EmitKept(span, 1, emit);
            }
            Interlocked.Add(ref _held, -held.Count);
            entry.Held = null;
        }
        entry.Verdict = TraceVerdict.Keep;
    }

    private void DecideHeld(TraceEntry entry, List<SpanRecord> emit)
    {
        if (entry.Held is not { } held)
        {
            return;
        }
        foreach (var (span, keepOneIn) in held)
        {
            DecideByHash(span, keepOneIn, emit);
        }
        Interlocked.Add(ref _held, -held.Count);
        entry.Held = null;
    }

    private void DecideByHash(SpanRecord span, int keepOneIn, List<SpanRecord> emit)
    {
        if (keepOneIn <= 1 || HashTraceId(span.TraceId) % (ulong)keepOneIn == 0)
        {
            Interlocked.Increment(ref _keptHead);
            emit.Add(keepOneIn <= 1 ? span : span with { SampleWeight = (uint)keepOneIn });
        }
        else
        {
            Interlocked.Increment(ref _dropped);
        }
    }

    private void EmitKept(SpanRecord span, uint weight, List<SpanRecord> emit)
    {
        Interlocked.Increment(ref _keptTail);
        emit.Add(span.SampleWeight == weight ? span : span with { SampleWeight = weight });
    }

    private Shard ShardFor(string traceId) => _shards[(int)(HashTraceId(traceId) % ShardCount)];

    public readonly record struct Policy(int KeepOneIn, TimeSpan SlowThreshold);

    private enum TraceVerdict
    {
        /// <summary>Spans are held; no error/slow span seen yet.</summary>
        Pending,

        /// <summary>An error/slow span was seen: every span of the trace is kept at weight 1.</summary>
        Keep,

        /// <summary>The hold window ended with nothing notable: each span follows its own 1-in-N hash.</summary>
        Healthy,
    }

    private sealed class TraceEntry(string traceId, TraceVerdict verdict)
    {
        public string TraceId { get; } = traceId;

        public TraceVerdict Verdict { get; set; } = verdict;

        public List<(SpanRecord Span, int KeepOneIn)>? Held { get; set; }
    }

    private sealed class Shard
    {
        public Dictionary<string, TraceEntry> Entries { get; } = [];

        public Queue<(long DueTicks, TraceEntry Entry)> Holds { get; } = new();

        public Queue<(long DueTicks, TraceEntry Entry)> Expiry { get; } = new();
    }
}
