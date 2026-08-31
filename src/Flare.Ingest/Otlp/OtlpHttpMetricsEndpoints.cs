using System.Text;
using Flare.Ingest.Sinks;
using Flare.Ingest.Stats;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Metrics.V1;

namespace Flare.Ingest.Otlp;

/// <summary>
/// HTTP OTLP metrics receiver - <c>POST /v1/metrics</c> per the OTLP/HTTP spec, served on
/// the "otlp-http" endpoint (conventionally port 4318). Content-negotiates protobuf and
/// JSON, same shape as <see cref="OtlpHttpTraceEndpoints"/>.
/// </summary>
public static class OtlpHttpMetricsEndpoints
{
    private const string ProtobufContentType = "application/x-protobuf";
    private const string JsonContentType = "application/json";

    public static IEndpointRouteBuilder MapOtlpHttpMetricsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1/metrics", HandleExportAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleExportAsync(
        HttpContext http,
        IMetricEventSink sink,
        IIngestionStatsTracker stats,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // OtlpHttpMetricsEndpoints is a static class (it's an extension-method container,
        // not a service), so it can't be used as ILogger<T>'s type argument - a named
        // category via ILoggerFactory is the standard way to get a scoped logger here,
        // same as OtlpHttpTraceEndpoints.
        var logger = loggerFactory.CreateLogger("Flare.Ingest.Otlp.OtlpHttpMetricsEndpoints");

        var contentType = http.Request.ContentType ?? string.Empty;
        var isJson = contentType.Contains(JsonContentType, StringComparison.OrdinalIgnoreCase);
        var isProtobuf = contentType.Contains(ProtobufContentType, StringComparison.OrdinalIgnoreCase);

        if (!isJson && !isProtobuf && contentType.Length > 0)
        {
            await stats.RecordRejectedAsync(IngestionSignal.Metrics, IngestionProtocol.Http, "unsupported-media-type", cancellationToken);
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        ExportMetricsServiceRequest request;
        long byteCount;
        try
        {
            if (isJson)
            {
                using var reader = new StreamReader(http.Request.Body);
                var json = await reader.ReadToEndAsync(cancellationToken);
                byteCount = Encoding.UTF8.GetByteCount(json);
                request = JsonParser.Default.Parse<ExportMetricsServiceRequest>(json);
            }
            else
            {
                // Kestrel's request body stream disallows synchronous reads - see
                // OtlpHttpTraceEndpoints for the full explanation of this buffering step.
                using var buffer = new MemoryStream();
                await http.Request.Body.CopyToAsync(buffer, cancellationToken);
                byteCount = buffer.Length;
                buffer.Position = 0;
                request = ExportMetricsServiceRequest.Parser.ParseFrom(buffer);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Rejected malformed OTLP metrics export via HTTP ({ContentType})", isJson ? "json" : "protobuf");
            await stats.RecordRejectedAsync(IngestionSignal.Metrics, IngestionProtocol.Http, $"invalid-payload:{ex.GetType().Name}", cancellationToken);
            return Results.BadRequest();
        }

        // Captured once per request, not per data point - see LogEvent.IngestedAt's
        // remarks and ADR-0014.
        var ingestedAt = timeProvider.GetUtcNow();

        var result = OtlpMetricsMapper.Map(request, ingestedAt);
        foreach (var point in result.Points)
        {
            await sink.WriteAsync(point, cancellationToken);
        }

        await stats.RecordAcceptedAsync(IngestionSignal.Metrics, IngestionProtocol.Http, result.Points.Count, byteCount, cancellationToken);
        await stats.RecordServiceBreakdownAsync(
            IngestionSignal.Metrics,
            ServiceBreakdown.Build(result.Points.Select(p => (p.ServiceName, ClockSkew.Nanos(ingestedAt, p.Time))), byteCount),
            cancellationToken);

        if (result.UnsupportedMetricNames.Count > 0)
        {
            logger.LogWarning(
                "Dropped data points for {Count} metric(s) with an unsupported point type (ExponentialHistogram/Summary not yet supported): {Names}",
                result.UnsupportedMetricNames.Count,
                string.Join(", ", result.UnsupportedMetricNames));
        }

        logger.LogDebug("Ingested {Count} metric data point(s) via HTTP ({ContentType})", result.Points.Count, isJson ? "json" : "protobuf");

        var response = new ExportMetricsServiceResponse();
        return isJson
            ? Results.Text(JsonFormatter.Default.Format(response), JsonContentType)
            : Results.Bytes(response.ToByteArray(), ProtobufContentType);
    }
}
