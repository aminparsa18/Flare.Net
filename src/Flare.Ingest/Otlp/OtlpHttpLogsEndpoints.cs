using System.Text;
using Flare.Ingest.Sinks;
using Flare.Ingest.Stats;
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
        IIngestionStatsTracker stats,
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

        if (!isJson && !isProtobuf && contentType.Length > 0)
        {
            await stats.RecordRejectedAsync(IngestionSignal.Logs, IngestionProtocol.Http, "unsupported-media-type", cancellationToken);
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        ExportLogsServiceRequest request;
        long byteCount;
        try
        {
            if (isJson)
            {
                using var reader = new StreamReader(http.Request.Body);
                var json = await reader.ReadToEndAsync(cancellationToken);
                byteCount = Encoding.UTF8.GetByteCount(json);
                request = JsonParser.Default.Parse<ExportLogsServiceRequest>(json);
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
                request = ExportLogsServiceRequest.Parser.ParseFrom(buffer);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Previously unhandled here - a malformed body took down the request with a bare
            // 500 and left no record anywhere that it happened. Now: a clean 400, a debug
            // trail in the logs, and a counted+listed entry on the dashboard's Ingestion page.
            logger.LogWarning(ex, "Rejected malformed OTLP logs export via HTTP ({ContentType})", isJson ? "json" : "protobuf");
            await stats.RecordRejectedAsync(IngestionSignal.Logs, IngestionProtocol.Http, $"invalid-payload:{ex.GetType().Name}", cancellationToken);
            return Results.BadRequest();
        }

        var count = 0;
        foreach (var logEvent in OtlpLogMapper.Map(request))
        {
            await sink.WriteAsync(logEvent, cancellationToken);
            count++;
        }

        await stats.RecordAcceptedAsync(IngestionSignal.Logs, IngestionProtocol.Http, count, byteCount, cancellationToken);

        logger.LogDebug("Ingested {Count} log record(s) via HTTP ({ContentType})", count, isJson ? "json" : "protobuf");

        var response = new ExportLogsServiceResponse();
        return isJson
            ? Results.Text(JsonFormatter.Default.Format(response), JsonContentType)
            : Results.Bytes(response.ToByteArray(), ProtobufContentType);
    }
}
