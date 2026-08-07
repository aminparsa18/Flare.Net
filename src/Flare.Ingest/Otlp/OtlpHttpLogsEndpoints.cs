using Flare.Ingest.Sinks;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Flare.Ingest.Otlp;

/// <summary>
/// HTTP OTLP logs receiver - <c>POST /v1/logs</c> per the OTLP/HTTP spec, served on the
/// "otlp-http" endpoint (conventionally port 4318). Content-negotiates protobuf and JSON.
/// </summary>
public static class OtlpHttpLogsEndpoints
{
    private const string ProtobufContentType = "application/x-protobuf";
    private const string JsonContentType = "application/json";

    public static IEndpointRouteBuilder MapOtlpHttpLogsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/logs", HandleExportAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleExportAsync(
        HttpContext http,
        ILogEventSink sink,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // OtlpHttpLogsEndpoints is a static class (it's an extension-method container, not a
        // service), so it can't be used as ILogger<T>'s type argument - a named category via
        // ILoggerFactory is the standard way to get a scoped logger in this pattern.
        var logger = loggerFactory.CreateLogger("Flare.Ingest.Otlp.OtlpHttpLogsEndpoints");

        var contentType = http.Request.ContentType ?? string.Empty;
        var isJson = contentType.Contains(JsonContentType, StringComparison.OrdinalIgnoreCase);
        var isProtobuf = contentType.Contains(ProtobufContentType, StringComparison.OrdinalIgnoreCase);

        ExportLogsServiceRequest request;
        if (isJson)
        {
            using var reader = new StreamReader(http.Request.Body);
            var json = await reader.ReadToEndAsync(cancellationToken);
            request = JsonParser.Default.Parse<ExportLogsServiceRequest>(json);
        }
        else if (isProtobuf || contentType.Length == 0)
        {
            // Google.Protobuf's MessageParser.ParseFrom(Stream) reads synchronously, but
            // Kestrel's request body stream disallows synchronous reads (AllowSynchronousIO
            // is false by default) and throws InvalidOperationException. Buffer the body into
            // memory asynchronously first, then parse from that in-memory stream instead.
            using var buffer = new MemoryStream();
            await http.Request.Body.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            request = ExportLogsServiceRequest.Parser.ParseFrom(buffer);
        }
        else
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        var count = 0;
        foreach (var logEvent in OtlpLogMapper.Map(request))
        {
            await sink.WriteAsync(logEvent, cancellationToken);
            count++;
        }

        logger.LogDebug("Ingested {Count} log record(s) via HTTP ({ContentType})", count, isJson ? "json" : "protobuf");

        var response = new ExportLogsServiceResponse();
        return isJson
            ? Results.Text(JsonFormatter.Default.Format(response), JsonContentType)
            : Results.Bytes(response.ToByteArray(), ProtobufContentType);
    }
}