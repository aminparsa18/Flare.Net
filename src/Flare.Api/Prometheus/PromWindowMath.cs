namespace Flare.Api.Prometheus;

/// <summary>
/// The arithmetic behind <c>deriv</c>, <c>predict_linear</c> and the subquery forms of <c>delta</c>, <c>changes</c>
/// and <c>resets</c> (ADR-0246), over a window already reduced to values. Pure and I/O-free.
/// </summary>
internal static class PromWindowMath
{
    /// <summary>
    /// Least-squares line through (x, y). <paramref name="xs"/> are seconds relative to the instant the line is
    /// evaluated at, so <c>Intercept</c> is the fitted value there. Null with fewer than two points or when every x is equal.
    /// </summary>
    public static (double Slope, double Intercept)? Fit(IReadOnlyList<double> xs, IReadOnlyList<double> ys)
    {
        var n = xs.Count;
        if (n < 2)
        {
            return null;
        }

        var xMean = xs.Average();
        var yMean = ys.Average();
        double sxx = 0;
        double sxy = 0;
        for (var i = 0; i < n; i++)
        {
            var dx = xs[i] - xMean;
            sxx += dx * dx;
            sxy += dx * (ys[i] - yMean);
        }

        if (sxx == 0)
        {
            return null;
        }

        var slope = sxy / sxx;
        return (slope, yMean - slope * xMean);
    }

    /// <summary>
    /// Prometheus's double exponential smoothing (<c>holt_winters</c>): a smoothed level and trend over the values,
    /// <paramref name="sf"/> weighting the level and <paramref name="tf"/> the trend. Null with fewer than two values.
    /// </summary>
    public static double? HoltWinters(IReadOnlyList<double> values, double sf, double tf)
    {
        if (values.Count < 2)
        {
            return null;
        }

        double s0 = 0;
        var s1 = values[0];
        var b = values[1] - values[0];
        for (var i = 1; i < values.Count; i++)
        {
            var x = sf * values[i];
            // The trend is the first difference for the first step, then smoothed.
            b = i - 1 == 0 ? b : tf * (s1 - s0) + (1 - tf) * b;
            var y = (1 - sf) * (s1 + b);
            s0 = s1;
            s1 = x + y;
        }

        return s1;
    }

    /// <summary>
    /// The share of observations between <paramref name="lower"/> and <paramref name="upper"/>, from histogram buckets,
    /// assuming observations are spread evenly inside a bucket. Null when the histogram is empty.
    /// </summary>
    public static double? HistogramFraction(double lower, double upper, SortedDictionary<(double Lower, double Upper), double> buckets)
    {
        var total = buckets.Values.Sum();
        if (total <= 0)
        {
            return null;
        }

        return lower >= upper ? 0 : (CountBelow(upper, buckets) - CountBelow(lower, buckets)) / total;
    }

    private static double CountBelow(double x, SortedDictionary<(double Lower, double Upper), double> buckets)
    {
        double below = 0;
        foreach (var ((lo, hi), count) in buckets)
        {
            if (hi <= x)
            {
                below += count;
            }
            else if (lo < x && hi > lo)
            {
                below += count * (x - lo) / (hi - lo);
            }
        }

        return below;
    }

    /// <summary>How many times consecutive values differ; NaN equals NaN, as in Prometheus.</summary>
    public static double Changes(IReadOnlyList<double> values)
    {
        var count = 0;
        for (var i = 1; i < values.Count; i++)
        {
            if (values[i] != values[i - 1] && !(double.IsNaN(values[i]) && double.IsNaN(values[i - 1])))
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>How many times a value is lower than the one before it.</summary>
    public static double Resets(IReadOnlyList<double> values)
    {
        var count = 0;
        for (var i = 1; i < values.Count; i++)
        {
            if (values[i] < values[i - 1])
            {
                count++;
            }
        }

        return count;
    }
}
