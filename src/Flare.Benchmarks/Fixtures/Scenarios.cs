using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Flare.Api.Json;
using Microsoft.AspNetCore.Http;
using Flare.Api.Model;
using Flare.Benchmarks.Fixtures.Synthetic;
using MemoryPack;

namespace Flare.Benchmarks.Fixtures;

/// <summary>
/// One response payload the dashboard actually requests, held as the real <c>Flare.Api.Model</c>
/// object so both codecs encode exactly the same value. Every benchmark that takes a
/// <c>Scenario</c> parameter (and the TypeScript side, via <see cref="Export.FixtureExporter"/>)
/// iterates <see cref="Scenarios.All"/>.
/// </summary>
public abstract class Scenario
{
    /// <summary>Stable id used in benchmark parameters, file names and the report, e.g. <c>logs-page.captured</c>.</summary>
    public required string Name { get; init; }

    /// <summary><c>captured</c> (recorded from a running stack) or <c>synthetic</c> (generated in code).</summary>
    public required string Source { get; init; }

    public required string Description { get; init; }

    /// <summary>The endpoint whose response this is, for the report.</summary>
    public required string Endpoint { get; init; }

    public abstract byte[] EncodeJson();

    public abstract byte[] EncodeMemoryPack();

    public abstract object? DecodeJson(byte[] bytes);

    public abstract object? DecodeMemoryPack(byte[] bytes);

    /// <summary>The production response path: <see cref="ApiSerialization.Write{T}"/>, which picks the codec from the request's <c>Accept</c> header.</summary>
    public abstract IResult Write(HttpContext http);

    /// <summary>Rows/spans/points, for the report's scenario table.</summary>
    public required string Shape { get; init; }
}

public sealed class Scenario<T>(T value, JsonTypeInfo<T> jsonTypeInfo) : Scenario
{
    public T Value { get; } = value;

    public override byte[] EncodeJson() => JsonSerializer.SerializeToUtf8Bytes(Value, jsonTypeInfo);

    public override byte[] EncodeMemoryPack() => MemoryPackSerializer.Serialize(Value);

    public override object? DecodeJson(byte[] bytes) => JsonSerializer.Deserialize(bytes, jsonTypeInfo);

    public override object? DecodeMemoryPack(byte[] bytes) => MemoryPackSerializer.Deserialize<T>(bytes);

    public override IResult Write(HttpContext http) => ApiSerialization.Write(http, Value, jsonTypeInfo);
}

/// <summary>
/// The scenario registry. Captured scenarios load the JSON a running Flare.Api returned
/// (<c>Fixtures/Captured/*.json</c>, embedded in this assembly; see the README for how they were
/// recorded) and are skipped when that file is missing, so the project still builds and runs
/// before anything has been captured.
/// </summary>
public static class Scenarios
{
    private static readonly Lazy<IReadOnlyList<Scenario>> AllLazy = new(Build);

    public static IReadOnlyList<Scenario> All => AllLazy.Value;

    /// <summary>For BenchmarkDotNet's <c>[ParamsSource]</c>.</summary>
    public static IEnumerable<string> Names => All.Select(s => s.Name);

    public static Scenario Get(string name) =>
        All.FirstOrDefault(s => s.Name == name) ?? throw new ArgumentException($"Unknown scenario '{name}'. Known: {string.Join(", ", Names)}");

    /// <summary>
    /// A representative <c>POST /api/logs/search</c> request body - the request direction of
    /// ADR-0015. Small, so it is only measured as a codec component, never compressed.
    /// </summary>
    public static Scenario<LogSearchRequest> LogSearchRequest { get; } = new(
        new LogSearchRequest
        {
            Filter = new LogFilter
            {
                From = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero),
                To = new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero),
                Services = ["checkout-api", "payments-worker"],
                SeverityNumbers = [17, 18, 19, 20, 21],
                Search = "timeout",
                Attributes = [new AttributeFilter { Key = "http.route", Value = "/api/orders/{id}" }],
            },
            PageSize = 200,
            IncludeSpanDuration = true,
        },
        LogsJsonContext.Default.LogSearchRequest)
    {
        Name = "logs-request.synthetic",
        Source = "synthetic",
        Description = "Log search request body with a typical filter",
        Endpoint = "POST /api/logs/search (request)",
        Shape = "1 filter",
    };

    private static List<Scenario> Build()
    {
        var page = LogEventDtoFixtures.Page(new Random(42), 200);
        var scenarios = new List<Scenario>
        {
            new Scenario<LogSearchResponse>(page, LogsJsonContext.Default.LogSearchResponse)
            {
                Name = "logs-page.synthetic",
                Source = "synthetic",
                Description = "Log search page, modeled OTel attribute bags (Typical shape)",
                Endpoint = "POST /api/logs/search",
                Shape = $"{page.Events.Count} rows",
            },
        };

        if (LoadCaptured("logs-page.json", LogsJsonContext.Default.LogSearchResponse) is { } logs)
        {
            scenarios.Add(new Scenario<LogSearchResponse>(logs, LogsJsonContext.Default.LogSearchResponse)
            {
                Name = "logs-page.captured",
                Source = "captured",
                Description = "Log search page recorded from the seeded example stack",
                Endpoint = "POST /api/logs/search",
                Shape = $"{logs.Events.Count} rows",
            });
        }

        if (LoadCaptured("trace.json", SpansJsonContext.Default.TraceDto) is { } trace)
        {
            scenarios.Add(new Scenario<TraceDto>(trace, SpansJsonContext.Default.TraceDto)
            {
                Name = "trace.captured",
                Source = "captured",
                Description = "Trace detail (waterfall) recorded from the seeded example stack",
                Endpoint = "GET /api/traces/{traceId}",
                Shape = $"{trace.Spans.Count} spans",
            });
        }

        if (LoadCaptured("span-pool.json", SpansJsonContext.Default.TraceDto) is { } pool)
        {
            var large = AssembleTrace(pool);
            scenarios.Add(new Scenario<TraceDto>(large, SpansJsonContext.Default.TraceDto)
            {
                Name = "trace-large.synthetic",
                Source = "synthetic",
                Description = "Trace detail: 200 captured spans re-parented into one trace (seeded traces have <= 4 spans)",
                Endpoint = "GET /api/traces/{traceId}",
                Shape = $"{large.Spans.Count} spans",
            });
        }

        if (LoadCaptured("metrics.json",MetricsJsonContext.Default.MetricQueryResponse) is { } metrics)
        {
            scenarios.Add(new Scenario<MetricQueryResponse>(metrics, MetricsJsonContext.Default.MetricQueryResponse)
            {
                Name = "metrics.captured",
                Source = "captured",
                Description = "Metric query (grouped series) recorded from the seeded example stack",
                Endpoint = "POST /api/metrics/query",
                Shape = $"{metrics.Series.Count} series, {metrics.Series.Sum(s => s.Points.Count)} points",
            });
        }

        return scenarios;
    }

    /// <summary>
    /// One large trace out of real spans: every span keeps its own name, attributes, events and
    /// links, but gets the same trace id, a parent in a fan-out-of-3 tree, and start times 1 ms
    /// apart (duration kept) so the list stays in start order the way <see cref="TraceDto"/> is.
    /// </summary>
    private static TraceDto AssembleTrace(TraceDto pool)
    {
        const string traceId = "5a1d0c0ffee5a1d0c0ffee5a1d0c0ffe";
        var start = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero);
        var spans = pool.Spans
            .Select((span, i) =>
            {
                var begin = start.AddMilliseconds(i);
                return span with
                {
                    TraceId = traceId,
                    ParentSpanId = i == 0 ? string.Empty : pool.Spans[(i - 1) / 3].SpanId,
                    StartTime = begin,
                    EndTime = begin + (span.EndTime - span.StartTime),
                };
            })
            .ToList();

        return pool with { TraceId = traceId, Spans = spans };
    }

    private static T? LoadCaptured<T>(string fileName, JsonTypeInfo<T> jsonTypeInfo)
        where T : class
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"captured/{fileName}");
        return stream is null ? null : JsonSerializer.Deserialize(stream, jsonTypeInfo);
    }
}
