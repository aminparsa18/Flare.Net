using System.ComponentModel;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// Charts one metric as ASCII sparklines via Flare.Api's <c>POST /api/metrics/query</c> -
/// the CLI-native equivalent of the dashboard's Metrics chart (<c>metrics/+page.svelte</c>
/// / <c>MetricChart.svelte</c>). Reuses MetricsCommand's wire DTOs/JSON options. Resolves
/// <c>metricName</c> -&gt; its <c>type</c> via a scoped <c>POST /api/metrics/names</c> call
/// first, same as the dashboard always has a metric's type in hand (from a prior
/// <c>/api/metrics/names</c> response) before ever querying it - see
/// <c>MetricQueryRequest.Type</c>'s own C# remarks. Aggregation modes (Sum's sum/rate/
/// count, Histogram's percentiles/mean/p75/p95/max) and bucket-width picking are direct
/// ports of MetricChart.svelte's `buildLines`/`sumMode`/`histogramMode` and
/// <c>$lib/logs/bucket-width.ts</c>'s <c>pickBucketWidthSeconds</c>. Unit formatting is a
/// deliberately simplified port of <c>$lib/metrics/axis.ts</c>'s <c>resolveAxisScale</c> -
/// dimensionless/annotation/rate/percent handling carries over, but not its time/byte
/// scale-family conversion (ms&lt;-&gt;s, B&lt;-&gt;MB) - a raw value prints in the
/// producer's own declared unit as-is, never rescaled. Known v1 simplification, same
/// spirit as TraceWaterfall.svelte's own documented ones.
/// </summary>
internal sealed class MetricCommand : AsyncCommand<MetricCommand.Settings>
{
    private const int SparkWidth = 60;
    private const int MaxSeries = 5;
    private static readonly char[] SparkChars = ['▁', '▂', '▃', '▄', '▅', '▆', '▇', '█'];

    internal sealed class Settings : CommandSettings
    {
        [CommandArgument(0, "<METRIC_NAME>")]
        [Description("Exact metric name to chart, e.g. http.server.duration.")]
        public required string MetricName { get; init; }

        [CommandOption("-s|--service <NAME>")]
        [Description("Which service's metric to chart - required if more than one service emits this name.")]
        public string? Service { get; init; }

        [CommandOption("--group-by <KEY>")]
        [Description("DataPointAttributes key to group series by, collapsing every series sharing that key's value.")]
        public string? GroupBy { get; init; }

        [CommandOption("--mode <MODE>")]
        [Description("Sum: sum, rate (default), count. Histogram: percentiles (default), mean, p75, p95, max. Not valid for Gauge.")]
        public string? Mode { get; init; }

        [CommandOption("--since <RANGE>")]
        [Description("How far back to chart: 15m, 1h, 6h, 24h, 7d. Default 1h.")]
        public string Since { get; init; } = "1h";
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!FlareHome.IsInitialized)
        {
            AnsiConsole.MarkupLine("[grey]Not initialized yet - run `flare start` first.[/]");
            return 1;
        }

        if (!TracesCommand.TryParseSince(settings.Since, out var since))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't parse --since '{Markup.Escape(settings.Since)}' - expected e.g. 15m, 1h, 6h, 24h, 7d.");
            return 1;
        }

        var to = DateTimeOffset.UtcNow;
        var from = to - since;

        var port = FlareHome.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        MetricNameInfoWire metric;
        try
        {
            var namesResponse = await PostAsync<MetricNamesRequestWire, MetricNamesResponseWire>(
                http,
                "/api/metrics/names",
                new MetricNamesRequestWire { From = from, To = to, Services = settings.Service is null ? null : [settings.Service] },
                cancellationToken);
            if (namesResponse is null)
            {
                return 1;
            }

            var candidates = (namesResponse.Metrics ?? []).Where(m => m.MetricName == settings.MetricName).ToList();
            if (candidates.Count == 0)
            {
                AnsiConsole.MarkupLine(
                    $"[grey]No metric named '{Markup.Escape(settings.MetricName)}' in the last {Markup.Escape(settings.Since)}{(settings.Service is null ? "" : $" for service '{Markup.Escape(settings.Service)}'")} - try `flare metrics`.[/]");
                return 0;
            }

            if (candidates.Count > 1)
            {
                var services = string.Join(", ", candidates.Select(c => c.ServiceName).Distinct());
                AnsiConsole.MarkupLine(
                    $"[red]✗[/] '{Markup.Escape(settings.MetricName)}' is emitted by more than one service - pick one with --service: {Markup.Escape(services)}");
                return 1;
            }

            metric = candidates[0];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        if (!TryResolveMode(metric.Type, settings.Mode, out var mode, out var modeError))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] {Markup.Escape(modeError!)}");
            return 1;
        }

        var bucketWidthSeconds = PickBucketWidthSeconds((to - from).TotalSeconds);

        MetricQueryResponseWire? queryResponse;
        try
        {
            queryResponse = await PostAsync<MetricQueryRequestWire, MetricQueryResponseWire>(
                http,
                "/api/metrics/query",
                new MetricQueryRequestWire
                {
                    MetricName = metric.MetricName,
                    Type = metric.Type,
                    Filter = new MetricFilterWire { From = from, To = to, Services = [metric.ServiceName] },
                    BucketWidthSeconds = bucketWidthSeconds,
                    GroupByAttributeKey = string.IsNullOrWhiteSpace(settings.GroupBy) ? null : settings.GroupBy,
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        if (queryResponse is null)
        {
            return 1;
        }

        var series = queryResponse.Series ?? [];

        AnsiConsole.MarkupLine($"[bold]{Markup.Escape(metric.MetricName)}[/] [grey]({metric.Type})[/]");
        if (!string.IsNullOrEmpty(metric.Description))
        {
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(metric.Description)}[/]");
        }

        AnsiConsole.MarkupLine(
            $"[grey]{series.Count} series · {FormatBucketWidthSeconds(bucketWidthSeconds)} interval · mode: {ModeLabel(metric.Type, mode)}[/]");
        AnsiConsole.WriteLine();

        if (series.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No data in range.[/]");
            return 0;
        }

        foreach (var s in series.Take(MaxSeries))
        {
            RenderSeries(s, metric.Type, mode, bucketWidthSeconds, metric.Unit);
        }

        if (series.Count > MaxSeries)
        {
            AnsiConsole.MarkupLine($"[grey]+{series.Count - MaxSeries} more series not shown ({MaxSeries} max) - narrow --service/--group-by to see them.[/]");
        }

        return 0;
    }

    private static async Task<TResponse?> PostAsync<TRequest, TResponse>(HttpClient http, string path, TRequest body, CancellationToken cancellationToken)
        where TResponse : class
    {
        using var httpResponse = await http.PostAsJsonAsync(path, body, MetricsWireJsonOptions.Instance, cancellationToken);
        if (!httpResponse.IsSuccessStatusCode)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] POST {path} failed: {(int)httpResponse.StatusCode} {httpResponse.ReasonPhrase}");
            return null;
        }

        return await httpResponse.Content.ReadFromJsonAsync<TResponse>(MetricsWireJsonOptions.Instance, cancellationToken);
    }

    // Mirrors MetricChart.svelte's SumMode/HistogramMode - default 'rate' for Sum, default
    // 'percentiles' for Histogram, same reasoning its own remarks give (a raw per-bucket
    // Sum reads differently depending on which range happens to be selected; percentiles
    // is the fullest single-glance view of a histogram's shape).
    private static bool TryResolveMode(string type, string? requested, out string mode, out string? error)
    {
        var normalized = requested?.Trim().ToLowerInvariant();
        switch (type)
        {
            case "Gauge":
                if (normalized is not null)
                {
                    mode = "";
                    error = "Gauge metrics have no aggregation mode - --mode isn't valid here.";
                    return false;
                }

                mode = "value";
                error = null;
                return true;

            case "Sum":
                mode = normalized ?? "rate";
                if (mode is "sum" or "rate" or "count")
                {
                    error = null;
                    return true;
                }

                error = $"Unknown --mode '{requested}' for a Sum metric - expected one of: sum, rate, count.";
                return false;

            case "Histogram":
                mode = normalized ?? "percentiles";
                if (mode is "percentiles" or "mean" or "p75" or "p95" or "max")
                {
                    error = null;
                    return true;
                }

                error = $"Unknown --mode '{requested}' for a Histogram metric - expected one of: percentiles, mean, p75, p95, max.";
                return false;

            default:
                mode = normalized ?? "";
                error = null;
                return true;
        }
    }

    private static string ModeLabel(string type, string mode) => type switch
    {
        "Sum" => mode switch { "sum" => "Sum", "count" => "Count", _ => "Rate" },
        "Histogram" => mode switch
        {
            "mean" => "Mean",
            "p75" => "p75",
            "p95" => "p95",
            "max" => "Max (approx.)",
            _ => "Percentiles",
        },
        _ => "Gauge",
    };

    private static void RenderSeries(MetricSeriesWire series, string type, string mode, int bucketWidthSeconds, string? unit)
    {
        var label = SeriesLabel(series);

        if (type == "Histogram" && mode == "percentiles")
        {
            AnsiConsole.MarkupLine(Markup.Escape(label));
            foreach (var key in new[] { "p50", "p90", "p99" })
            {
                RenderLine($"  {key}", ExtractHistogramValues(series, key), unit);
            }

            return;
        }

        var values = ExtractValues(series, type, mode, bucketWidthSeconds);
        RenderLine(label, values, ModeUnit(type, mode, unit));
    }

    private static void RenderLine(string label, List<double?> values, string? unit)
    {
        var spark = BuildSparkline(values);
        var latest = values.LastOrDefault(v => v.HasValue);
        var latestText = latest.HasValue ? FormatMetricValue(latest.Value, unit) : "—";

        var plainLabel = label.Length > 30 ? label[..29] + "…" : label.PadRight(30);
        AnsiConsole.MarkupLine($"{Markup.Escape(plainLabel)}  {Markup.Escape(spark)}  [grey]latest={Markup.Escape(latestText)}[/]");
    }

    private static string SeriesLabel(MetricSeriesWire series)
    {
        if (series.Attributes.Count == 0)
        {
            return series.ServiceName;
        }

        var attrs = string.Join(", ", series.Attributes.Select(kv => $"{kv.Key}={kv.Value}"));
        return $"{series.ServiceName} · {attrs}";
    }

    private static List<double?> ExtractValues(MetricSeriesWire series, string type, string mode, int bucketWidthSeconds)
    {
        var points = series.Points.OrderBy(p => p.BucketStart).ToList();
        return type switch
        {
            "Sum" when mode == "count" => points.Select(p => (double?)p.Count).ToList(),
            "Sum" when mode == "rate" => points.Select(p => p.Value.HasValue ? p.Value.Value / Math.Max(bucketWidthSeconds, 1) : (double?)null).ToList(),
            "Histogram" when mode == "mean" => points.Select(p => p.Sum.HasValue && p.Count is > 0 ? p.Sum.Value / p.Count.Value : (double?)null).ToList(),
            "Histogram" when mode == "p75" => points.Select(p => p.P75).ToList(),
            "Histogram" when mode == "p95" => points.Select(p => p.P95).ToList(),
            "Histogram" when mode == "max" => points.Select(p => p.MaxApprox).ToList(),
            _ => points.Select(p => p.Value).ToList(), // Gauge, and Sum's "sum" mode.
        };
    }

    private static List<double?> ExtractHistogramValues(MetricSeriesWire series, string key)
    {
        var points = series.Points.OrderBy(p => p.BucketStart).ToList();
        return key switch
        {
            "p50" => points.Select(p => p.P50).ToList(),
            "p90" => points.Select(p => p.P90).ToList(),
            "p99" => points.Select(p => p.P99).ToList(),
            _ => [],
        };
    }

    // Rate mode changes the unit, not just the numbers ("By" -> "By/s") - same
    // MetricChart.svelte displayUnit reasoning. Count mode is a plain sample count, not
    // the metric's declared unit at all, so it gets no unit rather than an inherited,
    // misleading one.
    private static string? ModeUnit(string type, string mode, string? unit) => type switch
    {
        "Sum" when mode == "count" => null,
        "Sum" when mode == "rate" => $"{unit}/s",
        _ => unit,
    };

    // Port of $lib/logs/bucket-width.ts's pickBucketWidthSeconds.
    private static readonly int[] NiceWidthsSeconds = [1, 5, 10, 30, 60, 300, 900, 3600, 21600, 86400];

    private static int PickBucketWidthSeconds(double totalRangeSeconds)
    {
        if (totalRangeSeconds <= 0)
        {
            return NiceWidthsSeconds[0];
        }

        var idealWidth = totalRangeSeconds / 75;
        foreach (var width in NiceWidthsSeconds)
        {
            if (width >= idealWidth)
            {
                return width;
            }
        }

        return NiceWidthsSeconds[^1];
    }

    // Port of $lib/logs/bucket-width.ts's formatBucketWidthSeconds.
    private static string FormatBucketWidthSeconds(int seconds)
    {
        if (seconds < 60)
        {
            return $"{seconds}s";
        }

        if (seconds < 3600)
        {
            return $"{Math.Round(seconds / 60.0)}m";
        }

        if (seconds < 86400)
        {
            return $"{Math.Round(seconds / 3600.0)}h";
        }

        return $"{Math.Round(seconds / 86400.0)}d";
    }

    // Sparkline: one Unicode block character per point, quantized to 8 levels across the
    // series' own min..max - the well-known "spark" CLI convention. Downsamples to
    // SparkWidth points (even stride) when there are more, so a long --since window still
    // fits on one line instead of wrapping.
    private static string BuildSparkline(List<double?> values)
    {
        if (values.Count == 0)
        {
            return "";
        }

        var sampled = values.Count > SparkWidth ? Downsample(values, SparkWidth) : values;

        var present = sampled.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        if (present.Count == 0)
        {
            return new string(' ', sampled.Count);
        }

        var min = present.Min();
        var max = present.Max();
        var range = max - min;

        var chars = new char[sampled.Count];
        for (var i = 0; i < sampled.Count; i++)
        {
            if (!sampled[i].HasValue)
            {
                chars[i] = ' ';
                continue;
            }

            var level = range <= 0
                ? SparkChars.Length / 2
                : Math.Clamp((int)Math.Round((sampled[i]!.Value - min) / range * (SparkChars.Length - 1)), 0, SparkChars.Length - 1);
            chars[i] = SparkChars[level];
        }

        return new string(chars);
    }

    private static List<double?> Downsample(List<double?> values, int targetCount)
    {
        var result = new List<double?>(targetCount);
        for (var i = 0; i < targetCount; i++)
        {
            var index = (int)((long)i * values.Count / targetCount);
            result.Add(values[Math.Min(index, values.Count - 1)]);
        }

        return result;
    }

    // Deliberately simplified port of $lib/metrics/axis.ts's resolveAxisScale/
    // formatAtScale - dimensionless/annotation/rate/percent handling carries over, but not
    // the time/byte scale-family conversion (a value always prints in the producer's own
    // declared unit, never rescaled to e.g. ms<->s). See this file's own class remarks.
    private static string FormatMetricValue(double value, string? unit)
    {
        var u = (unit ?? "").Trim();
        var number = FormatNumber(value);
        if (u.Length == 0 || u == "1")
        {
            return number;
        }

        var slash = u.IndexOf('/');
        if (slash > 0)
        {
            var numerator = u[..slash].Trim();
            var denominator = u[(slash + 1)..].Trim();
            if (denominator.Length == 0)
            {
                denominator = "?";
            }

            var word = AnnotationWord(numerator);
            var suffix = word is not null ? $"{word}/{denominator}"
                : numerator.Length == 0 || numerator == "1" ? $"/{denominator}"
                : $"{numerator}/{denominator}";
            return $"{number} {suffix}";
        }

        var annotation = AnnotationWord(u);
        if (annotation is not null)
        {
            return $"{number} {annotation}";
        }

        return u == "%" ? $"{number}%" : $"{number} {u}";
    }

    private static string? AnnotationWord(string unit) =>
        unit.Length >= 2 && unit[0] == '{' && unit[^1] == '}' ? unit[1..^1] : null;

    private static string FormatNumber(double n)
    {
        var abs = Math.Abs(n);
        if (abs < 1000)
        {
            return n.ToString("0.##", CultureInfo.InvariantCulture);
        }

        string[] suffixes = ["", "K", "M", "B", "T"];
        var idx = 0;
        var v = n;
        while (Math.Abs(v) >= 1000 && idx < suffixes.Length - 1)
        {
            v /= 1000;
            idx++;
        }

        return $"{v.ToString("0.#", CultureInfo.InvariantCulture)}{suffixes[idx]}";
    }
}
