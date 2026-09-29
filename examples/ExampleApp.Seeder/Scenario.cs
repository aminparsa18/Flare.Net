using System.Text.Json.Nodes;

namespace ExampleApp.Seeder;

/// <summary>
/// One docs page's worth of backdated data. <see cref="Generate"/> fills the scenario's
/// <see cref="OtlpBatch"/>; <see cref="CreateObjectsAsync"/> creates whatever Flare.Api objects
/// the page shows (a dashboard, saved views, pipeline rules), named in the chosen locale.
/// </summary>
/// <remarks>
/// Scenarios that share service names keep their span names apart - e.g. <c>funnel</c>'s
/// storefront root is <c>GET /cart</c>, <c>external</c>'s is <c>GET /stores/nearby</c> - so
/// seeding both doesn't put one page's traces into the other's funnel or endpoint tables.
/// </remarks>
public abstract class Scenario
{
    public abstract string Name { get; }

    public abstract string Description { get; }

    /// <summary>The Flare.Api objects this scenario owns, in every locale - what gets deleted before re-seeding or on --clear.</summary>
    public virtual IEnumerable<(FlareApiClient.Kind Kind, Localized Name)> Objects => [];

    /// <summary>How long to wait between creating objects and sending telemetry - pipeline rules only apply once Flare.Ingest has polled them.</summary>
    public virtual TimeSpan IngestSettleTime => TimeSpan.Zero;

    public abstract void Generate(SeedContext context);

    public virtual Task CreateObjectsAsync(SeedContext context, CancellationToken ct) => Task.CompletedTask;
}

/// <summary>One name in each locale the docs screenshots are taken in.</summary>
public sealed record Localized(string En, string Ru, string ZhCn)
{
    public IEnumerable<string> All => [En, Ru, ZhCn];

    public string In(string locale) => locale switch
    {
        "ru" => Ru,
        "zh-CN" => ZhCn,
        _ => En,
    };
}

public sealed class SeedContext(string scenario, int windowMinutes, string locale, int seed, FlareApiClient api)
{
    /// <summary>Shapes the data (rates, shares, which trace fails) - seeded, so a scenario looks the same every run.</summary>
    public Random Rng { get; } = new(seed);

    /// <summary>Trace and span ids - never seeded, so --append runs don't reuse ids.</summary>
    public Random Ids { get; } = new();

    public OtlpBatch Batch { get; } = new(scenario);

    public FlareApiClient Api { get; } = api;

    public string Locale { get; } = locale;

    public int WindowMinutes { get; } = windowMinutes;

    public long NowNanos { get; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() * Otlp.NanosPerMs;

    public long StartNanos => NowNanos - WindowMinutes * 60L * Otlp.NanosPerSecond;

    /// <summary>Scales a per-hour count to the window.</summary>
    public int PerHour(double countPerHour) => Math.Max(1, (int)Math.Round(countPerHour * WindowMinutes / 60.0));

    /// <summary>The dashboard's time-range preset that covers the window.</summary>
    public string Preset => WindowMinutes switch
    {
        <= 15 => "15m",
        <= 60 => "1h",
        <= 360 => "6h",
        _ => "24h",
    };

    /// <summary>A uniformly random instant in the window, at least <paramref name="minSecondsAgo"/> before now.</summary>
    public long RandomTime(int minSecondsAgo = 20) =>
        NowNanos - (long)(Rng.NextDouble() * (WindowMinutes * 60 - minSecondsAgo) + minSecondsAgo) * Otlp.NanosPerSecond;

    /// <summary>Seconds since the start of the window for <paramref name="unixNanos"/>.</summary>
    public double Offset(long unixNanos) => (unixNanos - StartNanos) / (double)Otlp.NanosPerSecond;

    /// <summary>Evenly spaced sample times across the window, first at its start, last at or before now.</summary>
    public IEnumerable<long> Ticks(int stepSeconds)
    {
        for (var t = StartNanos; t <= NowNanos; t += stepSeconds * Otlp.NanosPerSecond)
        {
            yield return t;
        }
    }

    public string T(Localized text) => text.In(Locale);

    public double LogNormal(double median, double sigma)
    {
        var u1 = 1.0 - Rng.NextDouble();
        var u2 = Rng.NextDouble();
        return median * Math.Exp(sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2));
    }

    public double Gauss(double mean, double stdDev)
    {
        var u1 = 1.0 - Rng.NextDouble();
        var u2 = Rng.NextDouble();
        return mean + stdDev * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    public T Pick<T>(IReadOnlyList<T> items) => items[Rng.Next(items.Count)];

    // --- OTLP builders -----------------------------------------------------------------

    public const int KindInternal = 1;
    public const int KindServer = 2;
    public const int KindClient = 3;
    public const int KindProducer = 4;
    public const int KindConsumer = 5;

    /// <summary>Adds a span and returns its span id. <paramref name="error"/> sets status ERROR (with the message), otherwise status is <paramref name="okStatus"/>.</summary>
    public string Span(
        string resource, string scope, string traceId, string? parentSpanId, string name, int kind, long start, double durationMs,
        JsonArray? attributes = null, string? error = null, int okStatus = 1, JsonArray? events = null)
    {
        var spanId = Otlp.SpanId(Ids);
        var span = new JsonObject
        {
            ["traceId"] = traceId,
            ["spanId"] = spanId,
            ["parentSpanId"] = parentSpanId ?? "",
            ["name"] = name,
            ["kind"] = kind,
            ["startTimeUnixNano"] = Otlp.Nanos(start),
            ["endTimeUnixNano"] = Otlp.Nanos(start + (long)(durationMs * Otlp.NanosPerMs)),
            ["attributes"] = attributes ?? [],
            ["status"] = error is not null ? new JsonObject { ["code"] = 2, ["message"] = error } : new JsonObject { ["code"] = okStatus },
        };
        if (events is not null)
        {
            span["events"] = events;
        }

        Batch.AddSpan(resource, scope, span);
        return spanId;
    }

    public void Log(string resource, string scope, long time, int severityNumber, string body, JsonArray? attributes = null)
    {
        Batch.AddLog(resource, scope, new JsonObject
        {
            ["timeUnixNano"] = Otlp.Nanos(time),
            ["severityNumber"] = severityNumber,
            ["severityText"] = severityNumber switch
            {
                <= 4 => "Trace",
                <= 8 => "Debug",
                <= 12 => "Information",
                <= 16 => "Warning",
                <= 20 => "Error",
                _ => "Critical",
            },
            ["body"] = new JsonObject { ["stringValue"] = body },
            ["attributes"] = attributes ?? [],
        });
    }

    public static JsonObject Point(long time, double value, JsonArray? attributes = null, long? startTime = null, bool asInt = false)
    {
        var point = new JsonObject { ["timeUnixNano"] = Otlp.Nanos(time), ["attributes"] = attributes ?? [] };
        if (startTime is { } start)
        {
            point["startTimeUnixNano"] = Otlp.Nanos(start);
        }

        if (asInt)
        {
            point["asInt"] = ((long)value).ToString();
        }
        else
        {
            point["asDouble"] = value;
        }

        return point;
    }

    public static JsonObject Gauge(string name, string unit, string description, IEnumerable<JsonObject> points) => new()
    {
        ["name"] = name,
        ["unit"] = unit,
        ["description"] = description,
        ["gauge"] = new JsonObject { ["dataPoints"] = new JsonArray(points.ToArray<JsonNode>()) },
    };

    /// <summary>A cumulative (temporality 2) Sum.</summary>
    public static JsonObject Sum(string name, string unit, string description, IEnumerable<JsonObject> points, bool monotonic) => new()
    {
        ["name"] = name,
        ["unit"] = unit,
        ["description"] = description,
        ["sum"] = new JsonObject
        {
            ["aggregationTemporality"] = 2,
            ["isMonotonic"] = monotonic,
            ["dataPoints"] = new JsonArray(points.ToArray<JsonNode>()),
        },
    };

    public static JsonObject Histogram(string name, string unit, string description, IEnumerable<JsonObject> points, int temporality) => new()
    {
        ["name"] = name,
        ["unit"] = unit,
        ["description"] = description,
        ["histogram"] = new JsonObject
        {
            ["aggregationTemporality"] = temporality,
            ["dataPoints"] = new JsonArray(points.ToArray<JsonNode>()),
        },
    };

    /// <summary>A histogram data point over <paramref name="bounds"/> from raw observations.</summary>
    public static JsonObject HistogramPoint(long start, long time, double[] bounds, long[] bucketCounts, long count, double sum, JsonArray? attributes = null) => new()
    {
        ["startTimeUnixNano"] = Otlp.Nanos(start),
        ["timeUnixNano"] = Otlp.Nanos(time),
        ["count"] = count.ToString(),
        ["sum"] = sum,
        ["bucketCounts"] = new JsonArray(bucketCounts.Select(c => (JsonNode)c.ToString()).ToArray()),
        ["explicitBounds"] = new JsonArray(bounds.Select(b => (JsonNode)b).ToArray()),
        ["attributes"] = attributes ?? [],
    };

    public static int Bucket(double[] bounds, double value)
    {
        for (var i = 0; i < bounds.Length; i++)
        {
            if (value <= bounds[i])
            {
                return i;
            }
        }

        return bounds.Length;
    }
}
