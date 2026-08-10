using System.Text;
using Flare.Ingest.Sinks;
using Flare.Ingest.Stats;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Flare.Ingest.Otlp;

/// <summary>
/// HTTP OTLP traces receiver - <c>POST /v1/traces</c> per the OTLP/HTTP spec, served on
/// the "otlp-http" endpoint (conventionally port 4318). Content-negotiates protobuf and
/// JSON, same shape as <see cref="OtlpHttpLogsEndpoints"/>.
/// </summary>
public static class OtlpHttpTraceEndpoints
{
    private const string ProtobufContentType = "application/x-protobuf";
    private const string JsonContentType = "application/json";

    public static IEndpointRouteBuilder MapOtlpHttpTraceEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/traces", HandleExportAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleExportAsync(
        HttpContext http,
        ISpanEventSink sink,
        IIngestionStatsTracker stats,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // OtlpHttpTraceEndpoints is a static class (it's an extension-method container,
        // not a service), so it can't be used as ILogger<T>'s type argument - a named
        // category via ILoggerFactory is the standard way to get a scoped logger here,
        // same as OtlpHttpLogsEndpoints.
        var logger = loggerFactory.CreateLogger("Flare.Ingest.Otlp.OtlpHttpTraceEndpoints");

        var contentType = http.Request.ContentType ?? string.Empty;
        var isJson = contentType.Contains(JsonContentType, StringComparison.OrdinalIgnoreCase);
        var isProtobuf = contentType.Contains(ProtobufContentType, StringComparison.OrdinalIgnoreCase);

        if (!isJson && !isProtobuf && contentType.Length > 0)
        {
            await stats.RecordRejectedAsync(IngestionSignal.Traces, IngestionProtocol.Http, "unsupported-media-type", cancellationToken);
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        ExportTraceServiceRequest request;
        long byteCount;
        try
        {
            if (isJson)
            {
                using var reader = new StreamReader(http.Request.Body);
                var json = await reader.ReadToEndAsync(cancellationToken);
                byteCount = Encoding.UTF8.GetByteCount(json);
                request = JsonParser.Default.Parse<ExportTraceServiceRequest>(json);
            }
            else
            {
                // Google.Protobuf's MessageParser.ParseFrom(Stream) reads synchronously, but
                // Kestrel's request body stream disallows synchronous reads (AllowSynchronousIO
                // is false by default) and throws InvalidOperationException. Buffer the body into
                // memory asynchronously first, then parse from that in-memory stream instead.
                using var buffer = new MemoryStream();
                await http.Request.Body.CopyToAsync(buffer, cancellationToken);
                byteCount = buffer.Length;
                buffer.Position = 0;
                request = ExportTraceServiceRequest.Parser.ParseFrom(buffer);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Rejected malformed OTLP traces export via HTTP ({ContentType})", isJson ? "json" : "protobuf");
            await stats.RecordRejectedAsync(IngestionSignal.Traces, IngestionProtocol.Http, $"invalid-payload:{ex.GetType().Name}", cancellationToken);
            return Results.BadRequest();
        }

        var count = 0;
        foreach (var span in OtlpTraceMapper.Map(request))
        {
            await sink.WriteAsync(span, cancellationToken);
            count++;
        }

        await stats.RecordAcceptedAsync(IngestionSignal.Traces, IngestionProtocol.Http, count, byteCount, cancellationToken);

        logger.LogDebug("Ingested {Count} span(s) via HTTP ({ContentType})", count, isJson ? "json" : "protobuf");

        var response = new ExportTraceServiceResponse();
        return isJson
            ? Results.Text(JsonFormatter.Default.Format(response), JsonContentType)
            : Results.Bytes(response.ToByteArray(), ProtobufContentType);
    }
}
