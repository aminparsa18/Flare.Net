using Flare.Api.Model;

namespace Flare.Benchmarks.TestData;

/// <summary>
/// Deterministic, representative <see cref="LogEventDto"/>/<see cref="LogSearchResponse"/>
/// generators for the API-response serialization benchmarks
/// (<see cref="Benchmarks.ApiResponseSerializationBenchmarks"/>). Same attribute-bag
/// modeling as <see cref="LogEventFixtures"/> (they deliberately don't share a builder -
/// <see cref="Flare.Ingest.Model.LogEvent"/> and <see cref="LogEventDto"/> are independent
/// types on purpose, see this repo's CLAUDE.md), re-shaped for <see cref="LogEventDto"/>'s
/// all-non-nullable-string convention (an absent OTel value round-trips through ClickHouse
/// as <see cref="string.Empty"/>, never <see langword="null"/> - see that type's remarks).
/// </summary>
public static class LogEventDtoFixtures
{
    private static readonly string[] ServiceNames = ["checkout-api", "payments-worker", "notification-service", "inventory-api"];
    private static readonly string[] HttpMethods = ["GET", "POST", "PUT", "DELETE"];
    private static readonly string[] HttpRoutes = ["/api/orders/{id}", "/api/cart", "/api/payments", "/api/users/{id}/profile"];
    private static readonly string[] SeverityTexts = ["INFO", "WARN", "ERROR", "DEBUG"];

    /// <summary>One row, as returned by <c>POST /api/logs/search</c>.</summary>
    public static LogEventDto One(Random random)
    {
        var now = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero).AddSeconds(random.Next(0, 86_400));
        var serviceName = Pick(random, ServiceNames);

        return new LogEventDto
        {
            EventId = Guid.NewGuid(),
            Timestamp = now,
            ObservedTimestamp = now.AddMilliseconds(random.Next(0, 50)),
            IngestedAt = now.AddMilliseconds(random.Next(50, 150)),
            TraceId = RandomHex(random, 32),
            SpanId = RandomHex(random, 16),
            TraceFlags = 1,
            SeverityText = Pick(random, SeverityTexts),
            SeverityNumber = (byte)random.Next(1, 25),
            ServiceName = serviceName,
            Body = $"Handled {Pick(random, HttpMethods)} {Pick(random, HttpRoutes)} in {random.Next(1, 500)}ms",
            ResourceSchemaUrl = "https://opentelemetry.io/schemas/1.27.0",
            ResourceAttributes = ResourceAttributes(random, serviceName),
            ScopeSchemaUrl = "https://opentelemetry.io/schemas/1.27.0",
            ScopeName = $"{serviceName}.instrumentation",
            ScopeVersion = "1.4.0",
            ScopeAttributes = new Dictionary<string, string>(),
            LogAttributes = LogAttributes(random),
            EventName = random.Next(0, 4) == 0 ? "http.server.request.failed" : string.Empty,
            PatternId = string.Empty,
            PatternTemplate = string.Empty,
            SpanDurationNano = random.Next(0, 2) == 0 ? (ulong?)null : (ulong)random.Next(1_000_000, 500_000_000),
        };
    }

    /// <summary>
    /// A full page as <c>Query.LogSearchQueryBuilder.DefaultPageSize</c> (200) actually
    /// returns - the genuine "batch" shape for this boundary (unlike the Redis-buffer
    /// side, a real <see cref="LogSearchResponse"/> really does carry many rows in one
    /// serialized payload).
    /// </summary>
    public static LogSearchResponse Page(Random random, int count) => new()
    {
        Events = Enumerable.Range(0, count).Select(_ => One(random)).ToList(),
        NextCursor = "eyJUaW1lc3RhbXAiOiIyMDI2LTA5LTA1VDEyOjAwOjAwWiIsIkV2ZW50SWQiOiJhYmMifQ==",
    };

    private static Dictionary<string, string> ResourceAttributes(Random random, string serviceName) => new()
    {
        ["service.name"] = serviceName,
        ["service.version"] = $"{random.Next(1, 6)}.{random.Next(0, 20)}.{random.Next(0, 20)}",
        ["service.instance.id"] = Guid.NewGuid().ToString(),
        ["deployment.environment"] = random.Next(0, 2) == 0 ? "production" : "staging",
        ["cloud.region"] = "us-east-1",
        ["host.name"] = $"ip-10-0-{random.Next(0, 255)}-{random.Next(0, 255)}",
    };

    private static Dictionary<string, string> LogAttributes(Random random) => new()
    {
        ["http.request.method"] = Pick(random, HttpMethods),
        ["http.route"] = Pick(random, HttpRoutes),
        ["http.response.status_code"] = (200 + random.Next(0, 5) * 100 + random.Next(0, 5)).ToString(),
        ["user.id"] = Guid.NewGuid().ToString(),
        ["request.id"] = Guid.NewGuid().ToString(),
    };

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
