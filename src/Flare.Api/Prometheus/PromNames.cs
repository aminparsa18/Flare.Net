using System.Text;
using Flare.Api.Model;

namespace Flare.Api.Prometheus;

/// <summary>What a Prometheus metric name selects once resolved to an OTel metric.</summary>
internal enum PromMetricKind
{
    /// <summary>A gauge's value.</summary>
    Gauge,

    /// <summary>A counter (OTel Sum): only meaningful under <c>rate()</c>/<c>increase()</c>.</summary>
    Counter,

    /// <summary><c>&lt;name&gt;_bucket</c> (or the bare name) of a histogram: input to <c>histogram_quantile</c>.</summary>
    HistogramBucket,

    /// <summary><c>&lt;name&gt;_sum</c> of a histogram.</summary>
    HistogramSum,

    /// <summary><c>&lt;name&gt;_count</c> of a histogram.</summary>
    HistogramCount,
}

internal sealed record PromMetric(string OtelName, MetricPointType Type, PromMetricKind Kind, string PromName);

/// <summary>
/// OTel → Prometheus metric and label name mapping, following the OpenTelemetry Prometheus
/// compatibility spec's naming rules (ADR-0109): characters outside <c>[a-zA-Z0-9_:]</c> become
/// <c>_</c>, a recognised unit appends its long name (<c>s</c> → <c>_seconds</c>), a Sum appends
/// <c>_total</c>, and a Histogram is addressed as <c>&lt;name&gt;_bucket</c>/<c>_sum</c>/<c>_count</c>.
/// Pure and I/O-free.
/// </summary>
internal static class PromNames
{
    private static readonly Dictionary<string, string> UnitSuffixes = new(StringComparer.Ordinal)
    {
        ["s"] = "seconds",
        ["ms"] = "milliseconds",
        ["us"] = "microseconds",
        ["ns"] = "nanoseconds",
        ["min"] = "minutes",
        ["h"] = "hours",
        ["d"] = "days",
        ["By"] = "bytes",
        ["KiBy"] = "kibibytes",
        ["MiBy"] = "mebibytes",
        ["GiBy"] = "gibibytes",
        ["TiBy"] = "tebibytes",
        ["By/s"] = "bytes_per_second",
        ["m"] = "meters",
        ["V"] = "volts",
        ["A"] = "amperes",
        ["J"] = "joules",
        ["W"] = "watts",
        ["Hz"] = "hertz",
        ["Cel"] = "celsius",
        ["%"] = "percent",
    };

    /// <summary>Replaces every character outside <c>[a-zA-Z0-9_:]</c> with <c>_</c> (and a leading digit gets a <c>_</c> prefix).</summary>
    public static string Sanitize(string name)
    {
        if (name.Length == 0)
        {
            return name;
        }

        var sb = new StringBuilder(name.Length + 1);
        if (char.IsDigit(name[0]))
        {
            sb.Append('_');
        }

        foreach (var c in name)
        {
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '_' or ':' ? c : '_');
        }

        return sb.ToString();
    }

    /// <summary>The Prometheus label name for an OTel attribute key.</summary>
    public static string LabelName(string attributeKey) => Sanitize(attributeKey);

    /// <summary>The base Prometheus name: sanitized, plus the unit suffix. No type suffix.</summary>
    public static string BaseName(string otelName, string? unit, MetricPointType type)
    {
        var name = Sanitize(otelName);
        if (string.IsNullOrEmpty(unit))
        {
            return name;
        }

        // "1" is a dimensionless gauge ("ratio"); on a counter it carries no information.
        var suffix = unit == "1"
            ? type == MetricPointType.Gauge ? "ratio" : null
            : UnitSuffixes.GetValueOrDefault(unit);
        return suffix is null || name.EndsWith("_" + suffix, StringComparison.Ordinal) ? name : $"{name}_{suffix}";
    }

    /// <summary>Every Prometheus name an OTel metric is addressable by.</summary>
    public static IEnumerable<(string PromName, PromMetricKind Kind)> PromNamesFor(string otelName, string? unit, MetricPointType type)
    {
        var baseName = BaseName(otelName, unit, type);
        switch (type)
        {
            case MetricPointType.Gauge:
                yield return (baseName, PromMetricKind.Gauge);
                break;
            case MetricPointType.Sum:
                yield return (baseName.EndsWith("_total", StringComparison.Ordinal) ? baseName : baseName + "_total", PromMetricKind.Counter);
                break;
            default:
                // Histogram and ExponentialHistogram. The bare name is accepted as an alias of _bucket
                // for histogram_quantile(q, rate(name[5m])), a convenience classic Prometheus lacks.
                yield return (baseName + "_bucket", PromMetricKind.HistogramBucket);
                yield return (baseName + "_sum", PromMetricKind.HistogramSum);
                yield return (baseName + "_count", PromMetricKind.HistogramCount);
                yield return (baseName, PromMetricKind.HistogramBucket);
                break;
        }
    }

    /// <summary>
    /// Resolves <paramref name="promName"/> against the metrics Flare knows. Null when none match;
    /// throws when it is ambiguous between point types.
    /// </summary>
    public static PromMetric? Resolve(string promName, IEnumerable<MetricNameInfo> known)
    {
        PromMetric? found = null;
        foreach (var info in known)
        {
            foreach (var (name, kind) in PromNamesFor(info.MetricName, info.Unit, info.Type))
            {
                if (name != promName)
                {
                    continue;
                }

                var candidate = new PromMetric(info.MetricName, info.Type, kind, promName);
                if (found is not null && (found.OtelName != candidate.OtelName || found.Type != candidate.Type))
                {
                    throw new PromQlException(
                        $"metric name '{promName}' is ambiguous: it maps to both {found.Type} '{found.OtelName}' and {candidate.Type} '{candidate.OtelName}'.");
                }

                found ??= candidate;
            }
        }

        return found;
    }
}
