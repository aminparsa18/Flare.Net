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
