namespace Flare.Api.Prometheus;

/// <summary>
/// PromQL binary-operator semantics over already-evaluated series (ADR-0239): arithmetic and comparison
/// between a vector and a scalar or between two vectors (matched by label signature, with
/// <c>on</c>/<c>ignoring</c> and <c>group_left</c>/<c>group_right</c>), and the set operators
/// <c>and</c>/<c>or</c>/<c>unless</c>. Pure and I/O-free, so it is unit-tested directly.
/// </summary>
/// <remarks>
/// Matching is per timestamp, as in Prometheus: two series that share a signature but never have a
/// sample at the same instant do not clash. A clash (two series on the "one" side with a sample at
/// the same instant) is a <see cref="PromQlException"/>, never a silent pick.
/// </remarks>
internal static class PromBinaryOperations
{
    /// <summary>One operator application. <c>Keep</c> is false only for a filtering comparison that is false.</summary>
    public static (double Value, bool Keep) Compute(string op, double left, double right, bool returnBool)
    {
        switch (op)
        {
            case "+": return (left + right, true);
            case "-": return (left - right, true);
            case "*": return (left * right, true);
            case "/": return (left / right, true);
            case "%": return (left % right, true);
            case "^": return (Math.Pow(left, right), true);
        }

        var holds = op switch
        {
            "==" => left == right,
            "!=" => left != right,
            ">" => left > right,
            "<" => left < right,
            ">=" => left >= right,
            "<=" => left <= right,
            _ => throw new PromQlException($"unsupported operator '{op}'."),
        };

        return returnBool ? (holds ? 1 : 0, true) : (left, holds);
    }

    /// <summary>The metric name is dropped by arithmetic and by <c>bool</c> comparisons; a filtering comparison and the set operators keep it.</summary>
    private static bool DropsName(PromBinary b) => !PromBinary.IsSetOperator(b.Op) && (!PromBinary.IsComparison(b.Op) || b.ReturnBool);

    public static List<PromSeries> ApplyScalar(PromBinary b, List<PromSeries> vector, double scalar, bool scalarOnLeft) =>
        ApplyScalar(b, vector, _ => scalar, scalarOnLeft);

    /// <summary>A vector against a scalar that changes per step (<c>time()</c>, <c>scalar(v)</c>): a sample with no scalar at its timestamp is dropped.</summary>
    public static List<PromSeries> ApplyScalar(PromBinary b, List<PromSeries> vector, List<PromSeries> scalar, bool scalarOnLeft)
    {
        var at = ScalarAt(scalar);
        return ApplyScalar(b, vector, t => at.TryGetValue(t, out var v) ? v : null, scalarOnLeft);
    }

    /// <summary>Arithmetic or a <c>bool</c> comparison between two scalars, at every timestamp both have.</summary>
    public static List<PromSeries> ApplyScalars(PromBinary b, List<PromSeries> left, List<PromSeries> right)
    {
        var rightAt = ScalarAt(right);
        var series = new PromSeries([]);
        foreach (var sample in left.SelectMany(s => s.Samples))
        {
            if (rightAt.TryGetValue(sample.TimestampMs, out var r))
            {
                series.Samples.Add(new PromSample(sample.TimestampMs, Compute(b.Op, sample.Value, r, b.ReturnBool).Value));
            }
        }

        return [series];
    }

    private static Dictionary<long, double> ScalarAt(List<PromSeries> scalar) =>
        scalar.SelectMany(s => s.Samples).ToDictionary(x => x.TimestampMs, x => x.Value);

    private static List<PromSeries> ApplyScalar(PromBinary b, List<PromSeries> vector, Func<long, double?> scalarAt, bool scalarOnLeft)
    {
        var dropName = DropsName(b);
        var result = new List<PromSeries>(vector.Count);
        foreach (var s in vector)
        {
            var series = new PromSeries(dropName ? WithoutName(s.Labels) : new Dictionary<string, string>(s.Labels));
            foreach (var sample in s.Samples)
            {
                if (scalarAt(sample.TimestampMs) is not { } scalar)
                {
                    continue;
                }

                var (l, r) = scalarOnLeft ? (scalar, sample.Value) : (sample.Value, scalar);
                var (value, keep) = Compute(b.Op, l, r, b.ReturnBool);
                if (!keep)
                {
                    continue;
                }

                // A filtering comparison keeps the vector's value whichever side the scalar is on.
                var isFilter = PromBinary.IsComparison(b.Op) && !b.ReturnBool;
                series.Samples.Add(new PromSample(sample.TimestampMs, isFilter ? sample.Value : value));
            }

            if (series.Samples.Count > 0)
            {
                result.Add(series);
            }
        }

        return result;
    }

    public static List<PromSeries> ApplyVectors(PromBinary b, List<PromSeries> left, List<PromSeries> right)
    {
        var matching = b.Matching;
        return PromBinary.IsSetOperator(b.Op)
            ? ApplySet(b.Op, matching, left, right)
            : ApplyMatched(b, matching, left, right);
    }

    private static List<PromSeries> ApplyMatched(PromBinary b, PromVectorMatching matching, List<PromSeries> left, List<PromSeries> right)
    {
        var oneToMany = matching.Cardinality == PromCardinality.OneToMany;
        var many = oneToMany ? right : left;
        var one = oneToMany ? left : right;
        var oneBySignature = one.GroupBy(s => Signature(s, matching)).ToDictionary(g => g.Key, g => g.ToList());
        var dropName = DropsName(b);
        var isFilter = PromBinary.IsComparison(b.Op) && !b.ReturnBool;

        // OneToOne results with the same label set (series that never overlap in time) are merged.
        var merged = new Dictionary<string, PromSeries>();
        var result = new List<PromSeries>();
        foreach (var group in many.GroupBy(s => Signature(s, matching)))
        {
            if (!oneBySignature.TryGetValue(group.Key, out var ones))
            {
                continue;
            }

            var oneAt = new Dictionary<long, (double Value, PromSeries Series)>();
            foreach (var o in ones)
            {
                foreach (var sample in o.Samples)
                {
                    if (!oneAt.TryAdd(sample.TimestampMs, (sample.Value, o)))
                    {
                        throw new PromQlException(
                            $"found duplicate series for the match group {{{group.Key.Replace('\0', ',')}}} on the {(oneToMany ? "left" : "right")} hand-side of the operation; "
                            + "many-to-one matching must be explicit (group_left/group_right) or use on()/ignoring() to narrow the match.");
                    }
                }
            }

            if (matching.Cardinality == PromCardinality.OneToOne)
            {
                var seen = new HashSet<long>();
                foreach (var sample in group.SelectMany(s => s.Samples))
                {
                    if (!seen.Add(sample.TimestampMs))
                    {
                        throw new PromQlException(
                            $"found duplicate series for the match group {{{group.Key.Replace('\0', ',')}}} on the left hand-side of the operation; "
                            + "many-to-one matching must be explicit (group_left/group_right) or use on()/ignoring() to narrow the match.");
                    }
                }
            }

            foreach (var m in group)
            {
                var samples = new List<PromSample>();
                PromSeries? oneSeries = null;
                foreach (var sample in m.Samples)
                {
                    if (!oneAt.TryGetValue(sample.TimestampMs, out var o))
                    {
                        continue;
                    }

                    oneSeries = o.Series;
                    var (l, r) = oneToMany ? (o.Value, sample.Value) : (sample.Value, o.Value);
                    var (value, keep) = Compute(b.Op, l, r, b.ReturnBool);
                    if (keep)
                    {
                        samples.Add(new PromSample(sample.TimestampMs, isFilter ? l : value));
                    }
                }

                if (samples.Count == 0)
                {
                    continue;
                }

                var labels = ResultLabels(m.Labels, oneSeries!.Labels, matching, dropName);
                if (matching.Cardinality == PromCardinality.OneToOne)
                {
                    var key = GroupKey(labels);
                    if (merged.TryGetValue(key, out var existing))
                    {
                        existing.Samples.AddRange(samples);
                        existing.Samples.Sort((x, y) => x.TimestampMs.CompareTo(y.TimestampMs));
                        continue;
                    }

                    var created = new PromSeries(labels);
                    created.Samples.AddRange(samples);
                    merged[key] = created;
                    result.Add(created);
                }
                else
                {
                    var created = new PromSeries(labels);
                    created.Samples.AddRange(samples);
                    result.Add(created);
                }
            }
        }

        return result;
    }

    private static Dictionary<string, string> ResultLabels(
        Dictionary<string, string> manyLabels, Dictionary<string, string> oneLabels, PromVectorMatching matching, bool dropName)
    {
        var labels = new Dictionary<string, string>(manyLabels, StringComparer.Ordinal);
        if (dropName)
        {
            labels.Remove("__name__");
        }

        if (matching.Cardinality == PromCardinality.OneToOne)
        {
            foreach (var key in labels.Keys.ToList())
            {
                if (matching.On ? !matching.Labels.Contains(key) : matching.Labels.Contains(key))
                {
                    labels.Remove(key);
                }
            }

            return labels;
        }

        // group_left(extra) / group_right(extra): copy the listed labels from the "one" side.
        foreach (var include in matching.Include)
        {
            if (oneLabels.TryGetValue(include, out var value))
            {
                labels[include] = value;
            }
            else
            {
                labels.Remove(include);
            }
        }

        return labels;
    }

    private static List<PromSeries> ApplySet(string op, PromVectorMatching matching, List<PromSeries> left, List<PromSeries> right)
    {
        var rightTimes = TimesBySignature(right, matching);
        var result = new List<PromSeries>();
        switch (op)
        {
            case "and":
            case "unless":
                foreach (var s in left)
                {
                    var times = rightTimes.GetValueOrDefault(Signature(s, matching));
                    var series = new PromSeries(new Dictionary<string, string>(s.Labels));
                    series.Samples.AddRange(s.Samples.Where(x => (times?.Contains(x.TimestampMs) ?? false) == (op == "and")));
                    if (series.Samples.Count > 0)
                    {
                        result.Add(series);
                    }
                }

                break;

            default:
                // or: every left sample, plus right samples whose signature has no left sample at that instant.
                var leftTimes = TimesBySignature(left, matching);
                result.AddRange(left.Where(s => s.Samples.Count > 0));
                foreach (var s in right)
                {
                    var times = leftTimes.GetValueOrDefault(Signature(s, matching));
                    var series = new PromSeries(new Dictionary<string, string>(s.Labels));
                    series.Samples.AddRange(s.Samples.Where(x => !(times?.Contains(x.TimestampMs) ?? false)));
                    if (series.Samples.Count > 0)
                    {
                        result.Add(series);
                    }
                }

                break;
        }

        return result;
    }

    private static Dictionary<string, HashSet<long>> TimesBySignature(List<PromSeries> series, PromVectorMatching matching)
    {
        var map = new Dictionary<string, HashSet<long>>();
        foreach (var s in series)
        {
            var key = Signature(s, matching);
            if (!map.TryGetValue(key, out var times))
            {
                times = [];
                map[key] = times;
            }

            foreach (var sample in s.Samples)
            {
                times.Add(sample.TimestampMs);
            }
        }

        return map;
    }

    /// <summary>The label set two series are matched on: only the <c>on</c> labels, or everything except the <c>ignoring</c> labels and <c>__name__</c>.</summary>
    private static string Signature(PromSeries s, PromVectorMatching matching) =>
        GroupKey(s.Labels.Where(kv => matching.On
            ? matching.Labels.Contains(kv.Key)
            : kv.Key != "__name__" && !matching.Labels.Contains(kv.Key)));

    internal static string GroupKey(IEnumerable<KeyValuePair<string, string>> labels) =>
        string.Join('\0', labels.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"));

    private static string GroupKey(Dictionary<string, string> labels) => GroupKey((IEnumerable<KeyValuePair<string, string>>)labels);

    private static Dictionary<string, string> WithoutName(Dictionary<string, string> labels) =>
        labels.Where(kv => kv.Key != "__name__").ToDictionary(kv => kv.Key, kv => kv.Value);
}
