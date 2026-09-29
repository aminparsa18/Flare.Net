using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace ExampleApp.Seeder;

/// <summary>
/// OTLP/JSON building blocks - the <c>resourceSpans</c>/<c>resourceMetrics</c>/<c>resourceLogs</c>
/// shapes Flare.Ingest's <c>:4318/v1/*</c> endpoints accept, with hex trace/span ids and 64-bit
/// integers as strings, per the OTLP/JSON spec.
/// </summary>
public static class Otlp
{
    public const long NanosPerMs = 1_000_000;
    public const long NanosPerSecond = 1_000_000_000;

    public static JsonObject Attr(string key, object value) => new()
    {
        ["key"] = key,
        ["value"] = value switch
        {
            int or long => new JsonObject { ["intValue"] = Convert.ToInt64(value).ToString() },
            double or float => new JsonObject { ["doubleValue"] = Convert.ToDouble(value) },
            bool b => new JsonObject { ["boolValue"] = b },
            _ => new JsonObject { ["stringValue"] = value.ToString() },
        },
    };

    public static JsonArray Attrs(params (string Key, object Value)[] attributes) =>
        new(attributes.Select(a => (JsonNode)Attr(a.Key, a.Value)).ToArray());

    public static string TraceId(Random rng) => Hex(rng, 16);

    public static string SpanId(Random rng) => Hex(rng, 8);

    private static string Hex(Random rng, int bytes)
    {
        var buffer = new byte[bytes];
        rng.NextBytes(buffer);
        return Convert.ToHexStringLower(buffer);
    }

    public static string Nanos(long unixNanos) => unixNanos.ToString();
}

/// <summary>
/// Collects one scenario's telemetry per (resource, scope), then posts it in chunks. Every
/// resource gets a <c>flare.seed=&lt;scenario&gt;</c> attribute - what <see cref="ClickHouseCleaner"/>
/// deletes by, so clearing a scenario never touches live data or another scenario's rows.
/// </summary>
public sealed class OtlpBatch(string scenario)
{
    public const string SeedAttribute = "flare.seed";

    private readonly Dictionary<(string Resource, string Scope), JsonArray> _spans = [];
    private readonly Dictionary<(string Resource, string Scope), JsonArray> _metrics = [];
    private readonly Dictionary<(string Resource, string Scope), JsonArray> _logs = [];
    private readonly Dictionary<string, JsonArray> _resources = [];

    public int SpanCount => _spans.Values.Sum(a => a.Count);

    public string Summary()
    {
        static int Points(JsonNode? metric) =>
            metric?.AsObject().Where(p => p.Value is JsonObject o && o.ContainsKey("dataPoints")).Sum(p => p.Value!["dataPoints"]!.AsArray().Count) ?? 0;

        var bytes = _spans.Values.Concat(_metrics.Values).Concat(_logs.Values).Sum(a => a.ToJsonString().Length);
        return $"{SpanCount} spans, {_metrics.Values.Sum(a => a.Count)} metrics ({_metrics.Values.Sum(a => a.Sum(Points))} points), " +
               $"{_logs.Values.Sum(a => a.Count)} logs, {_resources.Count} resources, ~{bytes / 1024} KB";
    }

    /// <summary>A resource with <c>service.name</c> set - the common case.</summary>
    public string Service(string serviceName, params (string Key, object Value)[] extra) =>
        Resource([("service.name", serviceName), .. extra]);

    /// <summary>A resource with exactly these attributes (plus the seed marker) - e.g. a collector receiver's, which carries no service.name.</summary>
    public string Resource(params (string Key, object Value)[] attributes)
    {
        var attrs = Otlp.Attrs([.. attributes, (SeedAttribute, scenario)]);
        var key = attrs.ToJsonString();
        _resources.TryAdd(key, attrs);
        return key;
    }

    public void AddSpan(string resource, string scope, JsonObject span) => Get(_spans, resource, scope).Add(span);

    public void AddMetric(string resource, string scope, JsonObject metric) => Get(_metrics, resource, scope).Add(metric);

    public void AddLog(string resource, string scope, JsonObject log) => Get(_logs, resource, scope).Add(log);

    public async Task SendAsync(OtlpSender sender, CancellationToken ct)
    {
        await SendSignalAsync(sender, "traces", "resourceSpans", "scopeSpans", "spans", _spans, ct);
        await SendSignalAsync(sender, "metrics", "resourceMetrics", "scopeMetrics", "metrics", _metrics, ct);
        await SendSignalAsync(sender, "logs", "resourceLogs", "scopeLogs", "logRecords", _logs, ct);
    }

    private static JsonArray Get(Dictionary<(string, string), JsonArray> map, string resource, string scope)
    {
        if (!map.TryGetValue((resource, scope), out var items))
        {
            map[(resource, scope)] = items = [];
        }

        return items;
    }

    // One request per (resource, scope, ~4 MB of items) - well under Flare.Ingest's default
    // 64 MB OTLP request cap (Otlp__MaxRequestSizeBytes), and small enough that one slow
    // request doesn't look like a hang.
    private const int ChunkBytes = 4 * 1024 * 1024;

    private async Task SendSignalAsync(
        OtlpSender sender, string signal, string resourceKey, string scopeKey, string itemsKey,
        Dictionary<(string Resource, string Scope), JsonArray> map, CancellationToken ct)
    {
        foreach (var ((resource, scope), items) in map)
        {
            var chunk = new JsonArray();
            var bytes = 0;
            foreach (var item in items.ToArray())
            {
                var size = item!.ToJsonString().Length;
                if (chunk.Count > 0 && bytes + size > ChunkBytes)
                {
                    await sender.PostAsync(signal, Envelope(resource, scope, resourceKey, scopeKey, itemsKey, chunk), ct);
                    chunk = [];
                    bytes = 0;
                }

                chunk.Add(item.DeepClone());
                bytes += size;
            }

            if (chunk.Count > 0)
            {
                await sender.PostAsync(signal, Envelope(resource, scope, resourceKey, scopeKey, itemsKey, chunk), ct);
            }
        }
    }

    private JsonObject Envelope(string resource, string scope, string resourceKey, string scopeKey, string itemsKey, JsonArray items) => new()
    {
        [resourceKey] = new JsonArray(new JsonObject
        {
            ["resource"] = new JsonObject { ["attributes"] = _resources[resource].DeepClone() },
            [scopeKey] = new JsonArray(new JsonObject
            {
                ["scope"] = new JsonObject { ["name"] = scope },
                [itemsKey] = items,
            }),
        }),
    };
}

public sealed class OtlpSender(HttpClient http, Uri baseUri, string? ingestKey)
{
    public async Task PostAsync(string signal, JsonObject body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, $"v1/{signal}"))
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(ingestKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ingestKey);
        }

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"POST {request.RequestUri} failed with {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}");
        }
    }
}
