using Flare.Api.Query;

namespace Flare.Api.Tempo;

/// <summary>
/// Turns the <see cref="SpanCompareRow"/>s of a <c>compare()</c> query into Tempo's series (ADR-0226), which
/// <see cref="TempoMetricsSeries"/> then writes. Pure.
/// </summary>
/// <remarks>
/// Per attribute and per group: a series <c>{ __meta_type="selection", span.x="value" }</c> for each of the
/// <see cref="SpanCompare.TopN"/> most common values (<c>baseline</c> for the spans outside the selection), whose samples are the
/// spans per step; a series <c>{ __meta_type="selection_total", span.x=nil }</c> that counts every span that has the attribute,
/// whatever its value, so the share of a value can be computed; and, if either group has more values than were kept, one
/// <c>{ __meta_error="__too_many_values__", span.x=nil }</c> without samples. Every step has a sample, zero where nothing was counted.
/// </remarks>
public static class TempoCompareSeries
{
    public const string MetaType = "__meta_type";
    public const string MetaError = "__meta_error";
    public const string TooManyValues = "__too_many_values__";

    public static TempoMetricsResult Build(TempoMetricsPlan plan, IReadOnlyList<SpanCompareRow> rows, TempoMetricsWindow window)
    {
        var topN = plan.Compare?.TopN ?? TraceQlParser.DefaultCompareTopN;
        var series = new List<TempoMetricSeries>();
        foreach (var attribute in rows.GroupBy(r => r.Attribute, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var tooMany = false;
            var totals = new List<TempoMetricSeries>();
            foreach (var selection in new[] { false, true })
            {
                var group = attribute.Where(r => r.Selection == selection).ToArray();
                if (group.Length == 0)
                {
                    continue;
                }

                var type = selection ? "selection" : "baseline";
                var values = group
                    .Where(r => r.Value.Length > 0)
                    .GroupBy(r => r.Value, StringComparer.Ordinal)
                    .Select(g => (Value: g.Key, Rows: g.ToArray(), Weight: g.Sum(r => r.Weight)))
                    .OrderByDescending(v => v.Weight)
                    .ThenBy(v => v.Value, StringComparer.Ordinal)
                    .ToList();
                tooMany |= values.Count > topN;
                foreach (var (value, valueRows, _) in values.Take(topN))
                {
                    series.Add(new TempoMetricSeries([new TempoLabel(MetaType, type), new TempoLabel(attribute.Key, value)], Samples(valueRows, window)));
                }

                totals.Add(new TempoMetricSeries([new TempoLabel(MetaType, type + "_total"), TempoLabel.Nil(attribute.Key)], Samples(group, window)));
            }

            series.AddRange(totals);
            if (tooMany)
            {
                series.Add(new TempoMetricSeries([new TempoLabel(MetaError, TooManyValues), TempoLabel.Nil(attribute.Key)], []));
            }
        }

        return new TempoMetricsResult(series, null);
    }

    /// <summary>One sample per step, the summed weight of the rows in it.</summary>
    private static List<TempoSample> Samples(IEnumerable<SpanCompareRow> rows, TempoMetricsWindow window)
    {
        var byBucket = rows.GroupBy(r => r.Bucket).ToDictionary(g => g.Key, g => g.Sum(r => r.Weight));
        var samples = new List<TempoSample>(window.Steps);
        for (long bucket = 0; bucket < window.Steps; bucket++)
        {
            samples.Add(new TempoSample((window.FromSeconds + bucket * window.StepSeconds) * 1000, byBucket.GetValueOrDefault(bucket)));
        }

        return samples;
    }
}
