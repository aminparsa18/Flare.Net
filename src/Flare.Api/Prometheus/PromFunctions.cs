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

    /// <summary>
    /// <c>sort</c>/<c>sort_desc</c> order series by their newest value (NaN last), <c>sort_by_label</c> by label
    /// values in natural order. Only an instant query shows the order; a range query's series are unordered in Prometheus too.
    /// </summary>
    public static List<PromSeries> Sort(PromSort sort, List<PromSeries> input)
    {
        var descending = sort.Name.EndsWith("_desc", StringComparison.Ordinal);
        if (sort.Name.StartsWith("sort_by_label", StringComparison.Ordinal))
        {
            var comparer = Comparer<PromSeries>.Create((a, b) =>
            {
                foreach (var label in sort.Labels)
                {
                    var c = NaturalCompare(a.Labels.GetValueOrDefault(label, ""), b.Labels.GetValueOrDefault(label, ""));
                    if (c != 0)
                    {
                        return descending ? -c : c;
                    }
                }

                return 0;
            });
            return [.. input.OrderBy(s => s, comparer)];
        }

        double Newest(PromSeries s) => s.Samples.Count == 0 ? double.NaN : s.Samples[^1].Value;
        var withValue = input.Where(s => !double.IsNaN(Newest(s)));
        var ordered = descending ? withValue.OrderByDescending(Newest) : withValue.OrderBy(Newest);
        return [.. ordered, .. input.Where(s => double.IsNaN(Newest(s)))];
    }

    /// <summary>Orders "node2" before "node10": digit runs compare as numbers, everything else ordinally.</summary>
    private static int NaturalCompare(string a, string b)
    {
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
            {
                var si = i;
                var sj = j;
                while (i < a.Length && char.IsDigit(a[i]))
                {
                    i++;
                }

                while (j < b.Length && char.IsDigit(b[j]))
                {
                    j++;
                }

                var da = a[si..i].TrimStart('0');
                var db = b[sj..j].TrimStart('0');
                var byLength = da.Length.CompareTo(db.Length);
                var c = byLength != 0 ? byLength : string.CompareOrdinal(da, db);
                if (c != 0)
                {
                    return c;
                }

                continue;
            }

            if (a[i] != b[j])
            {
                return a[i].CompareTo(b[j]);
            }

            i++;
            j++;
        }

        return (a.Length - i).CompareTo(b.Length - j);
    }

    internal static PromSeries With(this PromSeries series, IEnumerable<PromSample> samples)
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
