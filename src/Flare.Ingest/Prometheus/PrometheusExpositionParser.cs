using System.Globalization;

namespace Flare.Ingest.Prometheus;

/// <summary>Prometheus's own metric-type vocabulary, as declared by a <c>&#35; TYPE</c> line.</summary>
public enum PrometheusMetricType
{
    /// <summary>No <c>&#35; TYPE</c> line seen for this metric - treated as a gauge by convention (same as every Prometheus consumer does).</summary>
    Untyped,
    Counter,
    Gauge,
    Histogram,
    Summary,
}

/// <summary>One parsed exposition-format sample line, before any job/resource attribution.</summary>
/// <param name="Name">The full sample name as written on the wire - for a histogram this includes the <c>_bucket</c>/<c>_sum</c>/<c>_count</c> suffix, not the base metric name.</param>
/// <param name="Labels">This sample's label set, including <c>le</c>/<c>quantile</c> where present - <see cref="PrometheusMetricsMapper"/> strips those out where they're structural rather than a real data-point attribute.</param>
/// <param name="TimestampMillis">Milliseconds since the Unix epoch, when the line carried an explicit timestamp; otherwise the caller (the scrape worker) supplies the scrape time.</param>
public sealed record PrometheusSample(
    string Name,
    IReadOnlyDictionary<string, string> Labels,
    double Value,
    long? TimestampMillis);

/// <summary>
/// The result of parsing one exposition-format payload: every sample line, plus the
/// declared type and HELP text per base metric name (keyed by the name exactly as it
/// appeared on the <c>&#35; TYPE</c>/<c>&#35; HELP</c> line - for histograms/summaries that's the
/// base name, e.g. <c>http_request_duration_seconds</c>, not any individual sample's
/// suffixed name).
/// </summary>
public sealed record PrometheusParseResult(
    IReadOnlyList<PrometheusSample> Samples,
    IReadOnlyDictionary<string, PrometheusMetricType> Types,
    IReadOnlyDictionary<string, string> HelpText);

/// <summary>
/// Parses the Prometheus text exposition format (version 0.0.4 - the classic
/// <c>&#35; HELP</c>/<c>&#35; TYPE</c>/<c>name{labels} value [timestamp]</c> shape every Prometheus
/// client library and exporter emits by default). OpenMetrics-only features - exemplars,
/// the <c>&#35; EOF</c> terminator, UTF-8 quoted metric names, native histograms - are
/// deliberately out of scope, same "v1 doesn't cover X" precedent
/// <see cref="Otlp.OtlpMetricsMapper"/> already set for ExponentialHistogram/Summary.
/// </summary>
/// <remarks>
/// Tolerant by design: a line this parser can't make sense of is skipped, not thrown -
/// this runs against third-party exporters Flare doesn't control, and one malformed line
/// (or a metric name Flare doesn't expect) shouldn't drop an entire scrape's worth of
/// otherwise-good samples. Kept a pure static parser with no resource/job attribution of
/// its own - see <see cref="PrometheusMetricsMapper"/> for that, same
/// parser/mapper split <see cref="Otlp.OtlpMetricsMapper"/> uses for the OTLP wire format.
/// </remarks>
public static class PrometheusExpositionParser
{
    public static PrometheusParseResult Parse(string text)
    {
        var samples = new List<PrometheusSample>();
        var types = new Dictionary<string, PrometheusMetricType>(StringComparer.Ordinal);
        var help = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line[0] == '#')
            {
                TryParseDirective(line, types, help);
                continue;
            }

            if (TryParseSample(line, out var sample))
            {
                samples.Add(sample);
            }
        }

        return new PrometheusParseResult(samples, types, help);
    }

    private static void TryParseDirective(
        string line,
        Dictionary<string, PrometheusMetricType> types,
        Dictionary<string, string> help)
    {
        // "# HELP <name> <text...>" / "# TYPE <name> <type>" - any other "#" line (a plain
        // comment, or a directive keyword we don't recognize) is silently ignored.
        var rest = line[1..].TrimStart();
        var keywordEnd = rest.IndexOf(' ');
        if (keywordEnd < 0)
        {
            return;
        }

        var keyword = rest[..keywordEnd];
        var afterKeyword = rest[(keywordEnd + 1)..].TrimStart();
        var nameEnd = afterKeyword.IndexOf(' ');
        if (nameEnd < 0)
        {
            return;
        }

        var name = afterKeyword[..nameEnd];
        var value = afterKeyword[(nameEnd + 1)..];

        switch (keyword)
        {
            case "TYPE" when TryParseType(value.Trim(), out var type):
                types[name] = type;
                break;
            case "HELP":
                help[name] = UnescapeHelpText(value);
                break;
        }
    }

    private static bool TryParseType(string value, out PrometheusMetricType type)
    {
        type = value switch
        {
            "counter" => PrometheusMetricType.Counter,
            "gauge" => PrometheusMetricType.Gauge,
            "histogram" => PrometheusMetricType.Histogram,
            "summary" => PrometheusMetricType.Summary,
            "untyped" => PrometheusMetricType.Untyped,
            _ => PrometheusMetricType.Untyped,
        };
        return true;
    }

    private static bool TryParseSample(string line, out PrometheusSample sample)
    {
        sample = null!;

        var pos = 0;
        var nameStart = pos;
        while (pos < line.Length && line[pos] != '{' && !char.IsWhiteSpace(line[pos]))
        {
            pos++;
        }

        if (pos == nameStart)
        {
            return false;
        }

        var name = line[nameStart..pos];

        var labels = EmptyLabels;
        if (pos < line.Length && line[pos] == '{')
        {
            if (!TryParseLabels(line, ref pos, out labels))
            {
                return false;
            }
        }

        while (pos < line.Length && char.IsWhiteSpace(line[pos]))
        {
            pos++;
        }

        var remainder = line[pos..].Trim();
        if (remainder.Length == 0)
        {
            return false;
        }

        var parts = remainder.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 2)
        {
            return false;
        }

        if (!TryParseValue(parts[0], out var value))
        {
            return false;
        }

        long? timestamp = null;
        if (parts.Length == 2)
        {
            if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ts))
            {
                return false;
            }

            timestamp = ts;
        }

        sample = new PrometheusSample(name, labels, value, timestamp);
        return true;
    }

    private static readonly Dictionary<string, string> EmptyLabels = [];

    private static bool TryParseLabels(string line, ref int pos, out Dictionary<string, string> labels)
    {
        labels = new Dictionary<string, string>(StringComparer.Ordinal);
        pos++; // skip '{'

        while (true)
        {
            while (pos < line.Length && (char.IsWhiteSpace(line[pos]) || line[pos] == ','))
            {
                pos++;
            }

            if (pos >= line.Length)
            {
                return false;
            }

            if (line[pos] == '}')
            {
                pos++;
                return true;
            }

            var keyStart = pos;
            while (pos < line.Length && line[pos] != '=' && !char.IsWhiteSpace(line[pos]))
            {
                pos++;
            }

            if (pos == keyStart || pos >= line.Length)
            {
                return false;
            }

            var key = line[keyStart..pos];

            while (pos < line.Length && char.IsWhiteSpace(line[pos]))
            {
                pos++;
            }

            if (pos >= line.Length || line[pos] != '=')
            {
                return false;
            }

            pos++; // skip '='

            while (pos < line.Length && char.IsWhiteSpace(line[pos]))
            {
                pos++;
            }

            if (pos >= line.Length || line[pos] != '"')
            {
                return false;
            }

            if (!TryParseQuotedValue(line, ref pos, out var value))
            {
                return false;
            }

            labels[key] = value;
        }
    }

    private static bool TryParseQuotedValue(string line, ref int pos, out string value)
    {
        value = "";
        pos++; // skip opening quote

        var sb = new System.Text.StringBuilder();
        while (pos < line.Length)
        {
            var c = line[pos];
            if (c == '"')
            {
                pos++;
                value = sb.ToString();
                return true;
            }

            if (c == '\\' && pos + 1 < line.Length)
            {
                var next = line[pos + 1];
                switch (next)
                {
                    case '"':
                        sb.Append('"');
                        pos += 2;
                        continue;
                    case '\\':
                        sb.Append('\\');
                        pos += 2;
                        continue;
                    case 'n':
                        sb.Append('\n');
                        pos += 2;
                        continue;
                }
            }

            sb.Append(c);
            pos++;
        }

        return false; // ran off the end without a closing quote
    }

    /// <summary>
    /// HELP text uses the same backslash escaping as quoted label values, minus the
    /// quote escape (HELP text isn't quote-delimited) - unescaped left-to-right so a
    /// literal <c>\\n</c> (escaped backslash followed by a literal "n") isn't
    /// misread as a newline the way two blind <see cref="string.Replace(string,string)"/>
    /// calls would.
    /// </summary>
    private static string UnescapeHelpText(string text)
    {
        var sb = new System.Text.StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                var next = text[i + 1];
                if (next == 'n')
                {
                    sb.Append('\n');
                    i++;
                    continue;
                }

                if (next == '\\')
                {
                    sb.Append('\\');
                    i++;
                    continue;
                }
            }

            sb.Append(text[i]);
        }

        return sb.ToString();
    }

    private static bool TryParseValue(string token, out double value)
    {
        switch (token)
        {
            case "+Inf":
            case "Inf":
                value = double.PositiveInfinity;
                return true;
            case "-Inf":
                value = double.NegativeInfinity;
                return true;
            case "NaN":
                value = double.NaN;
                return true;
            default:
                return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
