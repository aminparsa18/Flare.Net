using System.Diagnostics;
using System.Globalization;
using OpenTelemetry.Trace;

namespace Flare.OpenTelemetry;

/// <summary>
/// Root sampler implementing OpenTelemetry's consistent-probability sampling: keeps a trace when
/// its 56-bit randomness is at or above the rejection threshold, and records that threshold in
/// <c>tracestate</c> as <c>ot=th:…</c> so Flare.Ingest can weight the kept spans (ADR-0279).
/// </summary>
/// <remarks>
/// <para>
/// Stand-in for <c>ConsistentProbabilitySampler</c> in <c>OpenTelemetry.Extensions</c>, which is
/// prerelease only; a stable package can't depend on it without NU5104. Switch to it once it
/// ships a stable release.
/// </para>
/// <para>
/// Meant as the root of a <see cref="ParentBasedSampler"/>, so it only decides traces that start
/// here. It reads randomness from <c>ot=rv:</c> when the parent context carries one and from the
/// trace id's low 56 bits otherwise (.NET generates random trace ids). The threshold is
/// <c>round((1 - p) × 2^56)</c>, written as 14 hex digits with trailing zeros dropped. Child spans
/// inherit the tracestate, so every span of a kept trace carries the same <c>th</c>.
/// </para>
/// </remarks>
internal sealed class ConsistentProbabilitySampler : Sampler
{
    private const int ThresholdDigits = 14;
    private const ulong MaxThreshold = 1UL << 56;
    private const ulong RandomnessMask = MaxThreshold - 1;

    private readonly ulong _threshold;
    private readonly string? _traceStateEntry;

    public ConsistentProbabilitySampler(double probability)
    {
        if (double.IsNaN(probability) || probability is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(probability), probability, "Probability must be between 0 and 1.");
        }

        _threshold = probability <= 0 ? MaxThreshold : (ulong)Math.Round((1 - probability) * MaxThreshold);
        _traceStateEntry = _threshold >= MaxThreshold ? null : "ot=th:" + EncodeThreshold(_threshold);
        Description = $"ConsistentProbabilitySampler{{{probability.ToString(CultureInfo.InvariantCulture)}}}";
    }

    internal ulong Threshold => _threshold;

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
    {
        if (_traceStateEntry is null)
        {
            return new SamplingResult(SamplingDecision.Drop);
        }

        var parentTraceState = samplingParameters.ParentContext.TraceState;
        var randomness = TryReadRandomness(parentTraceState) ?? TraceIdRandomness(samplingParameters.TraceId);
        if (randomness < _threshold)
        {
            return new SamplingResult(SamplingDecision.Drop);
        }

        return new SamplingResult(SamplingDecision.RecordAndSample, MergeTraceState(parentTraceState, _traceStateEntry));
    }

    /// <summary>14 hex digits, trailing zeros dropped; a zero threshold (keep everything) is "0".</summary>
    internal static string EncodeThreshold(ulong threshold)
    {
        var hex = threshold.ToString("x14", CultureInfo.InvariantCulture).TrimEnd('0');
        return hex.Length == 0 ? "0" : hex;
    }

    internal static ulong TraceIdRandomness(ActivityTraceId traceId)
    {
        Span<byte> bytes = stackalloc byte[16];
        traceId.CopyTo(bytes);
        ulong value = 0;
        for (var i = 9; i < 16; i++)
        {
            value = (value << 8) | bytes[i];
        }

        return value & RandomnessMask;
    }

    /// <summary>The <c>rv</c> sub-key of the first <c>ot</c> list-member, when present and well-formed.</summary>
    internal static ulong? TryReadRandomness(string? traceState)
    {
        if (string.IsNullOrEmpty(traceState))
        {
            return null;
        }

        foreach (var member in traceState.Split(','))
        {
            var trimmed = member.Trim(' ', '\t');
            if (!trimmed.StartsWith("ot=", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var entry in trimmed[3..].Split(';'))
            {
                if (entry.StartsWith("rv:", StringComparison.Ordinal)
                    && entry.Length == 3 + ThresholdDigits
                    && ulong.TryParse(entry.AsSpan(3), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rv))
                {
                    return rv;
                }
            }

            return null;
        }

        return null;
    }

    /// <summary>
    /// Sets <c>th</c> in the <c>ot</c> member, keeping its other sub-keys (such as <c>rv</c>) and
    /// every other vendor's member. The updated <c>ot</c> member goes first, as W3C asks for a
    /// modified entry.
    /// </summary>
    internal static string MergeTraceState(string? traceState, string thEntry)
    {
        if (string.IsNullOrEmpty(traceState))
        {
            return thEntry;
        }

        var th = thEntry[3..]; // "th:…"
        string? otValue = null;
        var others = new List<string>();
        foreach (var member in traceState.Split(','))
        {
            var trimmed = member.Trim(' ', '\t');
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (otValue is null && trimmed.StartsWith("ot=", StringComparison.Ordinal))
            {
                otValue = trimmed[3..];
            }
            else
            {
                others.Add(trimmed);
            }
        }

        var subKeys = otValue is null
            ? [th]
            : otValue.Split(';').Where(e => e.Length > 0 && !e.StartsWith("th:", StringComparison.Ordinal)).Prepend(th);
        others.Insert(0, "ot=" + string.Join(';', subKeys));
        return string.Join(',', others);
    }
}
