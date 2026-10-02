namespace Flare.Api.Alerting;

/// <summary>
/// Converts a value between two OTel/UCUM units of the same family (time or bytes), for an alert
/// rule's <see cref="Model.AlertRule.ThresholdUnit"/>. Shares <see cref="MetricUnitFormatter"/>'s
/// unit tables, so the formatter and converter can't disagree on what "ms" or "MiBy" means.
/// </summary>
public static class MetricUnitConverter
{
    /// <summary>True for a unit that has a conversion factor (time or bytes).</summary>
    public static bool IsConvertible(string? unit) => TryGetFactor(unit, out _, out _);

    /// <summary>
    /// Converts <paramref name="value"/> from <paramref name="fromUnit"/> to <paramref name="toUnit"/>.
    /// False when either unit isn't convertible or they are different families (ms vs By), leaving
    /// <paramref name="result"/> as <paramref name="value"/> unchanged.
    /// </summary>
    public static bool TryConvert(double value, string? fromUnit, string? toUnit, out double result)
    {
        result = value;
        if (!TryGetFactor(fromUnit, out var fromFamily, out var fromFactor)
            || !TryGetFactor(toUnit, out var toFamily, out var toFactor)
            || fromFamily != toFamily)
        {
            return false;
        }

        result = value * fromFactor / toFactor;
        return true;
    }

    /// <summary>
    /// A threshold typed in <paramref name="thresholdUnit"/>, expressed in the series' own
    /// <paramref name="seriesUnit"/>. Unchanged when no threshold unit is set, or the two units
    /// can't be converted (an unknown series unit, or a different family) - the value is then
    /// compared as typed, like a rule without a unit.
    /// </summary>
    public static double ToSeriesUnit(double value, string? thresholdUnit, string? seriesUnit) =>
        string.IsNullOrWhiteSpace(thresholdUnit) || !TryConvert(value, thresholdUnit, seriesUnit, out var converted)
            ? value
            : converted;

    private static bool TryGetFactor(string? unit, out char family, out double perBase)
    {
        var u = unit?.Trim() ?? "";
        if (MetricUnitFormatter.TimeUnitToSeconds.TryGetValue(u, out perBase))
        {
            family = 't';
            return true;
        }

        if (MetricUnitFormatter.ByteUnitToBytes.TryGetValue(u, out perBase))
        {
            family = 'b';
            return true;
        }

        family = default;
        perBase = 0;
        return false;
    }
}
