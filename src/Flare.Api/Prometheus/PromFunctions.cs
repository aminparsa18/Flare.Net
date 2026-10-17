namespace Flare.Api.Prometheus;

/// <summary>
/// The instant-vector math functions (ADR-0239): applied to every sample of every series, dropping the
/// metric name as Prometheus does. Pure and I/O-free.
/// </summary>
internal static class PromFunctions
{
    public static List<PromSeries> Apply(PromFunction fn, List<PromSeries> input)
    {
        if (fn.Name == "vector")
        {
            // vector(s) turns the scalar into an instant vector with no labels.
            return [new PromSeries([]).With(input[0].Samples)];
        }

        // clamp(v, min, max) is an empty vector when min > max, as in Prometheus.
        if (fn is { Name: "clamp", Parameters: [var lo, var hi] } && lo > hi)
        {
            return [];
        }

        var map = Map(fn);
        var result = new List<PromSeries>(input.Count);
        foreach (var s in input)
        {
            var series = new PromSeries(s.Labels.Where(kv => kv.Key != "__name__").ToDictionary(kv => kv.Key, kv => kv.Value));
            series.Samples.AddRange(s.Samples.Select(x => new PromSample(x.TimestampMs, map(x.Value))));
            result.Add(series);
        }

        return result;
    }

    private static PromSeries With(this PromSeries series, IEnumerable<PromSample> samples)
    {
        series.Samples.AddRange(samples);
        return series;
    }

    private static Func<double, double> Map(PromFunction fn) => fn.Name switch
    {
        "abs" => Math.Abs,
        "ceil" => Math.Ceiling,
        "floor" => Math.Floor,
        "sqrt" => Math.Sqrt,
        "exp" => Math.Exp,
        "ln" => Math.Log,
        "log2" => Math.Log2,
        "log10" => Math.Log10,
        "sgn" => v => double.IsNaN(v) ? double.NaN : Math.Sign(v),
        "round" => Round(fn.Parameters.Count > 0 ? fn.Parameters[0] : 1),
        "clamp" => v => Math.Max(fn.Parameters[0], Math.Min(fn.Parameters[1], v)),
        "clamp_min" => v => Math.Max(fn.Parameters[0], v),
        "clamp_max" => v => Math.Min(fn.Parameters[0], v),
        _ => throw new PromQlException($"unsupported function '{fn.Name}'."),
    };

    // Prometheus rounds half up to the nearest multiple of toNearest.
    private static Func<double, double> Round(double toNearest)
    {
        var inverse = 1 / toNearest;
        return v => Math.Floor(v * inverse + 0.5) / inverse;
    }
}
