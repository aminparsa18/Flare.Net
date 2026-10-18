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
/// <param name="Exemplar">The OpenMetrics exemplar written after the value (<c># {trace_id="..."} 0.4 1700000000.1</c>), when the line had one that parsed.</param>
public sealed record PrometheusSample(
    string Name,
    IReadOnlyDictionary<string, string> Labels,
    double Value,
    long? TimestampMillis,
    PrometheusExemplar? Exemplar = null);

/// <summary>An OpenMetrics exemplar: a sample measurement with its own label set (conventionally <c>trace_id</c>/<c>span_id</c>) and optional timestamp.</summary>
public sealed record PrometheusExemplar(
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
/// client library and exporter emits by default), plus the parts of OpenMetrics 1.0 a scrape of an
/// exemplar-emitting exporter needs (<paramref name="openMetrics"/> on <see cref="Parse"/>): exemplars,
/// second-resolution float timestamps, <c>_total</c> counter samples declared under their base name, and
/// the <c>_created</c> series. UTF-8 quoted metric names, native histograms and the other
/// OpenMetrics-only types stay out of scope, same "v1 doesn't cover X" precedent
/// <see cref="Otlp.OtlpMetricsMapper"/> already set for Summary.
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
    /// <param name="text">The response body.</param>
    /// <param name="openMetrics">True when the response was <c>application/openmetrics-text</c>: timestamps are then float seconds, and counters are declared under their base name while the samples carry <c>_total</c>.</param>
    public static PrometheusParseResult Parse(string text, bool openMetrics = false)
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
                TryParseDirective(line, openMetrics, types, help);
                continue;
            }

            if (TryParseSample(line, openMetrics, out var sample))
            {
                // OpenMetrics' _created series (the counter/histogram/summary start time) is metadata, not a
                // metric: stored as a gauge it would be a second, meaningless series per family.
                if (openMetrics && IsCreatedSeries(sample.Name, types))
                {
                    continue;
                }

                samples.Add(sample);
            }
        }

        return new PrometheusParseResult(samples, types, help);
    }

    private static bool IsCreatedSeries(string name, Dictionary<string, PrometheusMetricType> types)
    {
        const string suffix = "_created";
        return name.EndsWith(suffix, StringComparison.Ordinal)
            && types.TryGetValue(name[..^suffix.Length], out var type)
            && type is PrometheusMetricType.Counter or PrometheusMetricType.Histogram or PrometheusMetricType.Summary;
    }

    private static void TryParseDirective(
        string line,
        bool openMetrics,
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
                // OpenMetrics declares a counter under its base name ("# TYPE http_requests counter") but
                // writes the sample as http_requests_total, the name a classic scrape (and every existing
                // series and alert) uses - register that name so the mapper finds the type.
                if (openMetrics && type == PrometheusMetricType.Counter && !name.EndsWith("_total", StringComparison.Ordinal))
                {
                    types[name + "_total"] = type;
                }

                break;
            case "HELP":
                help[name] = UnescapeHelpText(value);
                if (openMetrics && !name.EndsWith("_total", StringComparison.Ordinal))
                {
                    help.TryAdd(name + "_total", help[name]);
                }

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

    private static bool TryParseSample(string line, bool openMetrics, out PrometheusSample sample)
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

        // Everything after the first '#' is the exemplar. The labels (the only place a '#' could be quoted
        // text) are already consumed, so this cannot cut a label value.
        PrometheusExemplar? exemplar = null;
        var hash = remainder.IndexOf('#');
        if (hash >= 0)
        {
            exemplar = TryParseExemplar(remainder[(hash + 1)..], openMetrics);
            remainder = remainder[..hash].Trim();
        }

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
            if (!TryParseTimestamp(parts[1], openMetrics, out var ts))
            {
                return false;
            }

            timestamp = ts;
        }

        sample = new PrometheusSample(name, labels, value, timestamp, exemplar);
        return true;
    }

    /// <summary>
    /// <c>{label="v",...} value [timestamp]</c>. A malformed exemplar is dropped, not the sample it rides on:
    /// the sample is the data, the exemplar is a link.
    /// </summary>
    private static PrometheusExemplar? TryParseExemplar(string text, bool openMetrics)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0 || trimmed[0] != '{')
        {
            return null;
        }

        var pos = 0;
        if (!TryParseLabels(trimmed, ref pos, out var labels))
        {
            return null;
        }

        var parts = trimmed[pos..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length is 0 or > 2 || !TryParseValue(parts[0], out var value) || !double.IsFinite(value))
        {
            return null;
        }

        long? timestamp = null;
        if (parts.Length == 2)
        {
            if (!TryParseTimestamp(parts[1], openMetrics, out var ts))
            {
                return null;
            }

            timestamp = ts;
        }

        return new PrometheusExemplar(labels, value, timestamp);
    }

    /// <summary>Classic text: integer milliseconds. OpenMetrics: float seconds (<c>1700000000.123</c>).</summary>
    private static bool TryParseTimestamp(string token, bool openMetrics, out long millis)
    {
        if (!openMetrics)
        {
            return long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out millis);
        }

        millis = 0;
        if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) || !double.IsFinite(seconds))
        {
            return false;
        }

        millis = (long)Math.Round(seconds * 1000d);
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
