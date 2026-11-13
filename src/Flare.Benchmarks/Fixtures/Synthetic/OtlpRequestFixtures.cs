using System.IO.Compression;
using Flare.Ingest.Model;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Logs.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

namespace Flare.Benchmarks.Fixtures.Synthetic;

/// <summary>
/// OTLP protobuf export requests as an SDK or the Collector sends them to Flare.Ingest, for
/// <see cref="Component.RequestDecompressionBenchmarks"/>: one batch of
/// <see cref="BatchSize"/> log records or spans, grouped per service the way an exporter groups
/// them by resource. Records come from <see cref="LogEventFixtures.Typical"/>, so the attribute
/// shapes match the Redis-buffer benchmarks.
/// </summary>
public static class OtlpRequestFixtures
{
    /// <summary>The OTel SDK batch processor's default <c>maxExportBatchSize</c>.</summary>
    public const int BatchSize = 512;

    public const string None = "none";

    /// <summary>
    /// Request <c>Content-Encoding</c>/<c>grpc-encoding</c> values under comparison, at the level a
    /// sender uses by default: Go's gzip default (6, the Collector's) and zstd's default (3).
    /// Decompression cost barely depends on the level the sender picked.
    /// </summary>
    public static readonly string[] Codecs = [None, "gzip-6", "zstd-3"];

    public static readonly string[] Names = ["logs", "traces"];

    public static byte[] Encode(string name) => name switch
    {
        "logs" => Logs().ToByteArray(),
        "traces" => Traces().ToByteArray(),
        _ => throw new ArgumentException($"Unknown OTLP request fixture '{name}'."),
    };

    public static byte[] Compress(byte[] data, string codec)
    {
        if (codec == None)
        {
            return data;
        }

        using var output = new MemoryStream(data.Length / 2);
        using (Stream stream = codec switch
        {
            "gzip-6" => new GZipStream(output, new ZLibCompressionOptions { CompressionLevel = 6 }, leaveOpen: true),
            "zstd-3" => new ZstandardStream(output, new ZstandardCompressionOptions { Quality = 3 }, leaveOpen: true),
            _ => throw new ArgumentException($"Unknown codec '{codec}'."),
        })
        {
            stream.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>The encoding name a sender puts on the wire for <paramref name="codec"/> (<c>gzip-6</c> → <c>gzip</c>).</summary>
    public static string EncodingName(string codec) => codec.Split('-')[0];

    private static IEnumerable<IGrouping<string, LogEvent>> Batch()
    {
        var random = new Random(20261113);
        return Enumerable.Range(0, BatchSize).Select(_ => LogEventFixtures.Typical(random)).GroupBy(e => e.ServiceName).ToList();
    }

    private static ExportLogsServiceRequest Logs()
    {
        var request = new ExportLogsServiceRequest();
        foreach (var service in Batch())
        {
            var first = service.First();
            var scope = new ScopeLogs { Scope = new InstrumentationScope { Name = first.ScopeName ?? string.Empty, Version = first.ScopeVersion ?? string.Empty } };
            scope.LogRecords.AddRange(service.Select(e =>
            {
                var record = new LogRecord
                {
                    TimeUnixNano = UnixNanos(e.Timestamp),
                    ObservedTimeUnixNano = UnixNanos(e.ObservedTimestamp ?? e.Timestamp),
                    SeverityNumber = (SeverityNumber)e.SeverityNumber,
                    SeverityText = e.SeverityText ?? string.Empty,
                    Body = new AnyValue { StringValue = e.Body ?? string.Empty },
                    TraceId = Hex(e.TraceId),
                    SpanId = Hex(e.SpanId),
                    Flags = 1,
                    EventName = e.EventName ?? string.Empty,
                };
                record.Attributes.AddRange(KeyValues(e.LogAttributes));
                return record;
            }));
            var resource = new ResourceLogs { Resource = Resource(first), SchemaUrl = first.ResourceSchemaUrl ?? string.Empty };
            resource.ScopeLogs.Add(scope);
            request.ResourceLogs.Add(resource);
        }

        return request;
    }

    private static ExportTraceServiceRequest Traces()
    {
        var request = new ExportTraceServiceRequest();
        foreach (var service in Batch())
        {
            var first = service.First();
            var scope = new ScopeSpans { Scope = new InstrumentationScope { Name = first.ScopeName ?? string.Empty, Version = first.ScopeVersion ?? string.Empty } };
            scope.Spans.AddRange(service.Select(e =>
            {
                var route = e.LogAttributes.GetValueOrDefault("http.route", "/");
                var span = new Span
                {
                    TraceId = Hex(e.TraceId),
                    SpanId = Hex(e.SpanId),
                    Name = $"{e.LogAttributes.GetValueOrDefault("http.request.method", "GET")} {route}",
                    Kind = Span.Types.SpanKind.Server,
                    StartTimeUnixNano = UnixNanos(e.Timestamp),
                    EndTimeUnixNano = UnixNanos(e.Timestamp) + (ulong)(e.SeverityNumber * 1_000_000),
                    Status = new Status { Code = e.SeverityNumber >= 17 ? Status.Types.StatusCode.Error : Status.Types.StatusCode.Unset },
                    Flags = 1,
                };
                span.Attributes.AddRange(KeyValues(e.LogAttributes));
                return span;
            }));
            var resource = new ResourceSpans { Resource = Resource(first), SchemaUrl = first.ResourceSchemaUrl ?? string.Empty };
            resource.ScopeSpans.Add(scope);
            request.ResourceSpans.Add(resource);
        }

        return request;
    }

    private static Resource Resource(LogEvent e)
    {
        var resource = new Resource();
        resource.Attributes.AddRange(KeyValues(e.ResourceAttributes));
        return resource;
    }

    private static IEnumerable<KeyValue> KeyValues(IReadOnlyDictionary<string, string> attributes) =>
        attributes.Select(kv => new KeyValue { Key = kv.Key, Value = new AnyValue { StringValue = kv.Value } });

    private static ByteString Hex(string? hex) => string.IsNullOrEmpty(hex) ? ByteString.Empty : ByteString.CopyFrom(Convert.FromHexString(hex));

    private static ulong UnixNanos(DateTimeOffset value) => (ulong)(value.ToUnixTimeMilliseconds() * 1_000_000);
}
