using System.Text.RegularExpressions;

namespace Flare.Api.Prometheus;

/// <summary>
/// <c>label_replace</c> and <c>label_join</c> (ADR-0240): rewrite one label of every series, keeping the
/// metric name as Prometheus does. Pure and I/O-free.
/// </summary>
internal static partial class PromLabelFunctions
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(250);

    [GeneratedRegex("^[a-zA-Z_][a-zA-Z0-9_]*$")]
    private static partial Regex LabelName();

    public static List<PromSeries> Apply(PromLabelFunction fn, List<PromSeries> input)
    {
        var dst = fn.Arguments[0];
        if (!LabelName().IsMatch(dst))
        {
            throw new PromQlException($"invalid destination label name in {fn.Name}(): {dst}");
        }

        Func<Dictionary<string, string>, string?> value;
        if (fn.Name == "label_replace")
        {
            var replacement = fn.Arguments[1];
            var src = fn.Arguments[2];
            Regex regex;
            try
            {
                regex = new Regex($"^(?:{fn.Arguments[3]})$", RegexOptions.CultureInvariant, RegexTimeout);
            }
            catch (ArgumentException ex)
            {
                throw new PromQlException($"invalid regular expression in label_replace(): {fn.Arguments[3]} ({ex.Message})");
            }

            // No match leaves the series alone (null); a match that expands to "" removes the label.
            value = labels =>
            {
                var m = regex.Match(labels.GetValueOrDefault(src, ""));
                return m.Success ? m.Result(replacement) : null;
            };
        }
        else
        {
            var separator = fn.Arguments[1];
            var sources = fn.Arguments.Skip(2).ToList();
            foreach (var src in sources.Where(s => !LabelName().IsMatch(s)))
            {
                throw new PromQlException($"invalid source label name in label_join(): {src}");
            }

            value = labels => string.Join(separator, sources.Select(s => labels.GetValueOrDefault(s, "")));
        }

        var result = new List<PromSeries>(input.Count);
        var seen = new HashSet<string>();
        foreach (var s in input)
        {
            var labels = new Dictionary<string, string>(s.Labels);
            try
            {
                var v = value(labels);
                if (v is not null)
                {
                    if (v.Length == 0)
                    {
                        labels.Remove(dst);
                    }
                    else
                    {
                        labels[dst] = v;
                    }
                }
            }
            catch (RegexMatchTimeoutException)
            {
                throw new PromQlException("label_replace() regular expression timed out.");
            }

            if (!seen.Add(string.Join(',', labels.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}={kv.Value}"))))
            {
                throw new PromQlException($"vector cannot contain metrics with the same labelset after {fn.Name}().");
            }

            var series = new PromSeries(labels);
            series.Samples.AddRange(s.Samples);
            result.Add(series);
        }

        return result;
    }
}
