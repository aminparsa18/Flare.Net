using Flare.Ingest.Model;

namespace Flare.Benchmarks.TestData;

/// <summary>
/// Deterministic, representative <see cref="LogEvent"/> generators for the Redis-buffer
/// serialization benchmarks (<see cref="Benchmarks.RedisBufferSerializationBenchmarks"/>).
/// </summary>
/// <remarks>
/// Attribute keys/cardinality are modeled on common OpenTelemetry semantic-convention
/// resource/log attributes (<c>service.*</c>, <c>host.*</c>, <c>k8s.*</c>, <c>http.*</c>) -
/// not lifted from a captured production payload (none was available), so treat the
/// absolute numbers as directionally representative rather than exact. <see cref="Seeded"/>
/// keeps every run/iteration byte-identical so the two codecs are compared on the exact
/// same input.
/// </remarks>
public static class LogEventFixtures
{
    private static readonly string[] ServiceNames = ["checkout-api", "payments-worker", "notification-service", "inventory-api"];
    private static readonly string[] Environments = ["production", "staging"];
    private static readonly string[] Regions = ["us-east-1", "eu-west-1", "ap-southeast-2"];
    private static readonly string[] HttpMethods = ["GET", "POST", "PUT", "DELETE"];
    private static readonly string[] HttpRoutes = ["/api/orders/{id}", "/api/cart", "/api/payments", "/api/users/{id}/profile"];
    private static readonly string[] SeverityTexts = ["INFO", "WARN", "ERROR", "DEBUG"];

    /// <summary>
    /// A single event with a "typical" attribute bag - roughly what a well-instrumented
    /// HTTP handler emits: a handful of resource attributes identifying the process, plus
    /// a handful of per-request log attributes.
    /// </summary>
    public static LogEvent Typical(Random random) => Build(random, resourceAttributeCount: 6, logAttributeCount: 5, longValues: false);

    /// <summary>
    /// A single event with a large, high-cardinality attribute bag - a noisier emitter
    /// (deep k8s/cloud resource metadata, a wide span of request-scoped log attributes
    /// with longer string values). Exercises the "batch-sized payload" side of the
    /// roadmap item's "representative attribute-bag sizes/cardinality" requirement
    /// without changing the wire shape (still one <see cref="LogEvent"/>).
    /// </summary>
    public static LogEvent AttributeHeavy(Random random) => Build(random, resourceAttributeCount: 24, logAttributeCount: 20, longValues: true);

    /// <summary>
    /// <paramref name="count"/> independent <see cref="Typical"/> events. Not how
    /// <see cref="Pipeline.RedisEventPayload"/> is actually invoked in production (one
    /// call per Redis Stream entry, one entry per event) - this exists purely to
    /// characterize how each codec's cost scales with payload size, per the roadmap
    /// item's explicit "batch-sized payloads" ask.
    /// </summary>
    public static List<LogEvent> Batch(Random random, int count)
    {
        var events = new List<LogEvent>(count);
        for (var i = 0; i < count; i++)
        {
            events.Add(Typical(random));
        }

        return events;
    }

    private static LogEvent Build(Random random, int resourceAttributeCount, int logAttributeCount, bool longValues)
    {
        var now = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero).AddSeconds(random.Next(0, 86_400));
        var serviceName = Pick(random, ServiceNames);

        return new LogEvent
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            ObservedTimestamp = now.AddMilliseconds(random.Next(0, 50)),
            IngestedAt = now.AddMilliseconds(random.Next(50, 150)),
            SeverityNumber = random.Next(1, 25),
            SeverityText = Pick(random, SeverityTexts),
            Body = longValues
                ? $"Request to {Pick(random, HttpRoutes)} failed after {random.Next(1, 30)} retries: connection reset by peer while calling downstream dependency; correlation-id={Guid.NewGuid()}"
                : $"Handled {Pick(random, HttpMethods)} {Pick(random, HttpRoutes)} in {random.Next(1, 500)}ms",
            TraceId = RandomHex(random, 32),
            SpanId = RandomHex(random, 16),
            TraceFlags = 1,
            ServiceName = serviceName,
            ResourceSchemaUrl = "https://opentelemetry.io/schemas/1.27.0",
            ResourceAttributes = ResourceAttributes(random, serviceName, resourceAttributeCount),
            ScopeSchemaUrl = "https://opentelemetry.io/schemas/1.27.0",
            ScopeName = $"{serviceName}.instrumentation",
            ScopeVersion = "1.4.0",
            ScopeAttributes = new Dictionary<string, string>(),
            LogAttributes = LogAttributes(random, logAttributeCount, longValues),
            EventName = random.Next(0, 4) == 0 ? "http.server.request.failed" : null,
        };
    }

    private static Dictionary<string, string> ResourceAttributes(Random random, string serviceName, int count)
    {
        var all = new (string Key, string Value)[]
        {
            ("service.name", serviceName),
            ("service.version", $"{random.Next(1, 6)}.{random.Next(0, 20)}.{random.Next(0, 20)}"),
            ("service.instance.id", Guid.NewGuid().ToString()),
            ("service.namespace", "flare-demo"),
            ("deployment.environment", Pick(random, Environments)),
            ("cloud.region", Pick(random, Regions)),
            ("cloud.provider", "aws"),
            ("cloud.availability_zone", $"{Pick(random, Regions)}a"),
            ("host.name", $"ip-10-0-{random.Next(0, 255)}-{random.Next(0, 255)}"),
            ("host.arch", "amd64"),
            ("os.type", "linux"),
            ("os.description", "Ubuntu 24.04.1 LTS"),
            ("k8s.cluster.name", "flare-prod"),
            ("k8s.namespace.name", "default"),
            ("k8s.pod.name", $"{serviceName}-{RandomHex(random, 10)}"),
            ("k8s.deployment.name", serviceName),
            ("k8s.node.name", $"ip-10-0-{random.Next(0, 255)}-{random.Next(0, 255)}.ec2.internal"),
            ("container.id", RandomHex(random, 64)),
            ("container.image.name", $"registry.internal/{serviceName}"),
            ("container.image.tag", $"v{random.Next(1, 200)}"),
            ("telemetry.sdk.name", "opentelemetry"),
            ("telemetry.sdk.language", "dotnet"),
            ("telemetry.sdk.version", "1.9.0"),
            ("process.pid", random.Next(1, 65535).ToString()),
        };

        var result = new Dictionary<string, string>(count);
        for (var i = 0; i < count && i < all.Length; i++)
        {
            result[all[i].Key] = all[i].Value;
        }

        return result;
    }

    private static Dictionary<string, string> LogAttributes(Random random, int count, bool longValues)
    {
        var result = new Dictionary<string, string>(count);
        var candidates = new (string Key, Func<string> Value)[]
        {
            ("http.request.method", () => Pick(random, HttpMethods)),
            ("http.route", () => Pick(random, HttpRoutes)),
            ("http.response.status_code", () => (200 + random.Next(0, 5) * 100 + random.Next(0, 5)).ToString()),
            ("http.request.duration_ms", () => random.Next(1, 2000).ToString()),
            ("user.id", () => Guid.NewGuid().ToString()),
            ("request.id", () => Guid.NewGuid().ToString()),
            ("client.address", () => $"203.0.113.{random.Next(0, 255)}"),
            ("network.peer.address", () => $"10.0.{random.Next(0, 255)}.{random.Next(0, 255)}"),
            ("error.type", () => "System.TimeoutException"),
            ("thread.id", () => random.Next(1, 64).ToString()),
            ("db.system", () => "clickhouse"),
            ("db.statement", () => longValues ? "SELECT * FROM orders WHERE customer_id = ? AND created_at > ? ORDER BY created_at DESC LIMIT 50" : "SELECT 1"),
            ("db.rows_affected", () => random.Next(0, 500).ToString()),
            ("messaging.system", () => "redis"),
            ("messaging.destination.name", () => "logs.buffer"),
            ("retry.count", () => random.Next(0, 5).ToString()),
            ("feature.flag.checkout_v2", () => (random.Next(0, 2) == 0).ToString()),
            ("cache.hit", () => (random.Next(0, 2) == 0).ToString()),
            ("upstream.name", () => "payments-gateway"),
            ("upstream.duration_ms", () => random.Next(1, 800).ToString()),
            ("correlation.id", () => Guid.NewGuid().ToString()),
        };

        for (var i = 0; i < count && i < candidates.Length; i++)
        {
            result[candidates[i].Key] = candidates[i].Value();
        }

        return result;
    }

    private static string Pick(Random random, string[] values) => values[random.Next(values.Length)];

    private static string RandomHex(Random random, int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = "0123456789abcdef"[random.Next(16)];
        }

        return new string(chars);
    }
}
