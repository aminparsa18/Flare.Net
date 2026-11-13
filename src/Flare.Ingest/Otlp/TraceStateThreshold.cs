using System.Globalization;

namespace Flare.Ingest.Otlp;

/// <summary>
/// Reads the OpenTelemetry consistent-probability sampling threshold (<c>ot=th:…</c>) from a
/// W3C <c>tracestate</c> and turns it into the span's adjusted count - how many spans the
/// sampled one stands for (ADR-0279).
/// </summary>
/// <remarks>
/// <para>
/// <c>th</c> is 1-14 hex digits of a 56-bit rejection threshold <c>T</c>, with trailing zeros
/// omitted (so <c>th:8</c> is <c>0x80000000000000</c>). The sampling probability is
/// <c>(2^56 - T) / 2^56</c> and the adjusted count is its inverse. <c>th:0</c> means "sampled with
/// probability 1". Written by the contrib .NET sampler, the Java and Go SDKs and the Collector's
/// probabilistic sampler.
/// </para>
/// <para>
/// <c>SampleWeight</c> is an integer (ADR-0122), so the count is rounded to the nearest whole
/// number. Common rates (1/2, 1/4, 1/10, 1/100) round to their exact N; an odd rate such as 40%
/// becomes 3 instead of 2.5. A missing, malformed or out-of-range value yields 1, the same weight
/// the span would have had before this existed. The legacy experimental <c>p:</c> value is not read.
/// </para>
/// </remarks>
public static class TraceStateThreshold
{
    private const int MaxThresholdDigits = 14;
    private const double MaxAdjustedCount = uint.MaxValue;
    private static readonly double TwoPow56 = Math.Pow(2, 56);

    /// <summary>Returns the adjusted count encoded by <paramref name="traceState"/>'s
    /// <c>ot</c> entry, or 1 when it carries no valid <c>th</c>.</summary>
    public static uint AdjustedCount(string? traceState)
    {
        if (string.IsNullOrEmpty(traceState) || !traceState.Contains("ot=", StringComparison.Ordinal))
        {
            return 1;
        }

        return TryReadThreshold(traceState, out var threshold) ? FromThreshold(threshold) : 1;
    }

    /// <summary>Adjusted count for a 56-bit rejection threshold, rounded and clamped to [1, <see cref="uint.MaxValue"/>].</summary>
    public static uint FromThreshold(ulong threshold)
    {
        if (threshold == 0)
        {
            return 1;
        }

        var count = Math.Round(TwoPow56 / (TwoPow56 - threshold), MidpointRounding.AwayFromZero);
        return count >= MaxAdjustedCount ? uint.MaxValue : Math.Max(1u, (uint)count);
    }

    private static bool TryReadThreshold(string traceState, out ulong threshold)
    {
        threshold = 0;

        // tracestate: comma-separated list-members, optional whitespace around each.
        foreach (var memberRange in traceState.AsSpan().Split(','))
        {
            var member = traceState.AsSpan()[memberRange].Trim(" \t");
            if (!member.StartsWith("ot="))
            {
                continue;
            }

            // The "ot" value: semicolon-separated key:value sub-entries. Only the first "ot" member counts.
            var value = member[3..];
            foreach (var entryRange in value.Split(';'))
            {
                var entry = value[entryRange];
                if (!entry.StartsWith("th:"))
                {
                    continue;
                }

                var digits = entry[3..];
                if (digits.Length is 0 or > MaxThresholdDigits
                    || !ulong.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var parsed))
                {
                    return false;
                }

                // Restore the omitted trailing zeros: shift left by 4 bits per missing digit.
                threshold = parsed << (4 * (MaxThresholdDigits - digits.Length));
                return true;
            }

            return false;
        }

        return false;
    }
}
