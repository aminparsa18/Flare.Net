namespace Flare.Api.Alerting;

/// <summary>
/// C# port of the dashboard's unit-aware value formatting (<c>src/dashboard/src/lib/metrics/axis.ts</c>'s
/// <c>resolveAxisScale</c>/<c>formatAtScale</c>), scoped to what <see cref="AlertMessageFormatter"/>
/// needs for a fired <see cref="Model.AlertConditionKind.MetricThreshold"/> alert's text: given the
/// metric's declared OTel/UCUM unit and the values actually being rendered, pick one display scale
/// (time -&gt; ns..h, bytes -&gt; B..TB, "%" as-is, everything else dimensionless) and format a raw value
/// at it. No "nice tick" step-sizing here (that's axis.ts's own concern for gridlines) - alert text
/// renders the observed/threshold values exactly, not an axis.
/// </summary>
public static class MetricUnitFormatter
{
    public readonly record struct Scale(double Factor, string Suffix);

    private readonly record struct ScaleStep(double PerBase, string Label);

    private static readonly IReadOnlyDictionary<string, double> TimeUnitToSeconds = new Dictionary<string, double>
    {
        ["ns"] = 1e-9,
        ["µs"] = 1e-6,
        ["us"] = 1e-6,
        ["ms"] = 1e-3,
        ["s"] = 1,
        ["min"] = 60,
        ["h"] = 3600,
        ["d"] = 86400,
    };

    private static readonly IReadOnlyDictionary<string, double> ByteUnitToBytes = new Dictionary<string, double>
    {
        ["By"] = 1,
        ["kBy"] = 1e3,
        ["MBy"] = 1e6,
        ["GBy"] = 1e9,
        ["TBy"] = 1e12,
        ["KiBy"] = 1024,
        ["MiBy"] = 1024d * 1024,
        ["GiBy"] = 1024d * 1024 * 1024,
        ["TiBy"] = 1024d * 1024 * 1024 * 1024,
    };

    // Ascending `PerBase` (i.e. descending physical unit size: h before ns) so PickScale can
    // walk from the biggest unit down and stop at the first one that reads >= 1.
    private static readonly ScaleStep[] TimeScales =
    [
        new(1.0 / 3600, "h"),
        new(1.0 / 60, "min"),
        new(1, "s"),
        new(1e3, "ms"),
        new(1e6, "µs"),
        new(1e9, "ns"),
    ];

    private static readonly ScaleStep[] ByteScales =
    [
        new(1.0 / (1024d * 1024 * 1024 * 1024), "TB"),
        new(1.0 / (1024d * 1024 * 1024), "GB"),
        new(1.0 / (1024d * 1024), "MB"),
        new(1.0 / 1024, "KB"),
        new(1, "B"),
    ];

    private static ScaleStep PickScale(ScaleStep[] scales, double peakInBase)
    {
        if (!(peakInBase > 0))
        {
            return scales[scales.Length / 2];
        }

        foreach (var scale in scales)
        {
            if (peakInBase * scale.PerBase >= 1)
            {
                return scale;
            }
        }

        return scales[^1];
    }

    /// <summary>"{exception}" -&gt; "exception"; null if <paramref name="unit"/> isn't a UCUM curly-brace annotation.</summary>
    private static string? AnnotationWord(string unit) =>
        unit.Length >= 2 && unit[0] == '{' && unit[^1] == '}' ? unit[1..^1] : null;

    /// <summary>
    /// Picks one display scale for a metric's declared unit, sized to <paramref name="peakAbs"/>
    /// (the largest |value| being formatted - e.g. <c>Math.Max(Math.Abs(observed), Math.Abs(threshold))</c>
    /// so both numbers in one alert message land on the same scale rather than each re-picking its own).
    /// Mirrors <c>axis.ts</c>'s <c>resolveAxisScale</c> unit table and rate ("&lt;numerator&gt;/&lt;denominator&gt;") splitting.
    /// </summary>
    public static Scale ResolveScale(string? unit, double peakAbs)
    {
        var u = (unit ?? string.Empty).Trim();

        var slash = u.IndexOf('/');
        if (slash > 0)
        {
            var numeratorUnit = u[..slash].Trim();
            var denominator = u[(slash + 1)..].Trim();
            if (denominator.Length == 0)
            {
                denominator = "?";
            }

            var word = AnnotationWord(numeratorUnit);
            if (word is not null || numeratorUnit is "" or "1")
            {
                return new Scale(1, word is not null ? $"{word}/{denominator}" : $"/{denominator}");
            }

            var numerator = ResolveScale(numeratorUnit, peakAbs);
            return new Scale(numerator.Factor, $"{numerator.Suffix}/{denominator}");
        }

        if (TimeUnitToSeconds.TryGetValue(u, out var secondsPerUnit))
        {
            var scale = PickScale(TimeScales, peakAbs * secondsPerUnit);
            return new Scale(secondsPerUnit * scale.PerBase, scale.Label);
        }

        if (ByteUnitToBytes.TryGetValue(u, out var bytesPerUnit))
        {
            var scale = PickScale(ByteScales, peakAbs * bytesPerUnit);
            return new Scale(bytesPerUnit * scale.PerBase, scale.Label);
        }

        if (u == "%")
        {
            return new Scale(1, "%");
        }

        // Dimensionless: no declared unit, UCUM "1" (ratio), or a curly-brace annotation
        // ("{exception}") - the count itself is the whole story, so no suffix.
        if (u is "" or "1" || AnnotationWord(u) is not null)
        {
            return new Scale(1, "");
        }

        // Unrecognized unit (e.g. "Cel", "V") - keep it as a literal suffix rather than
        // silently dropping context.
        return new Scale(1, u);
    }

    /// <summary>Formats a raw value (in the metric's declared unit) at a scale from <see cref="ResolveScale"/>.</summary>
    public static string Format(double raw, Scale scale)
    {
        var magnitude = (raw * scale.Factor).ToString("0.##");
        if (scale.Suffix.Length == 0)
        {
            return magnitude;
        }

        return scale.Suffix == "%" ? $"{magnitude}%" : $"{magnitude} {scale.Suffix}";
    }
}
