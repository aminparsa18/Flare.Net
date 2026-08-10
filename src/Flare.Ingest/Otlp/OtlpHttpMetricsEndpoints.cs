using Flare.Ingest.Sinks;
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

        ExportMetricsServiceRequest request;
        if (isJson)
        {
            using var reader = new StreamReader(http.Request.Body);
            var json = await reader.ReadToEndAsync(cancellationToken);
            request = JsonParser.Default.Parse<ExportMetricsServiceRequest>(json);
        }
        else if (isProtobuf || contentType.Length == 0)
        {
            // Kestrel's request body stream disallows synchronous reads - see
            // OtlpHttpTraceEndpoints for the full explanation of this buffering step.
            using var buffer = new MemoryStream();
            await http.Request.Body.CopyToAsync(buffer, cancellationToken);
            buffer.Position = 0;
            request = ExportMetricsServiceRequest.Parser.ParseFrom(buffer);
        }
        else
        {
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        var result = OtlpMetricsMapper.Map(request);
        foreach (var point in result.Points)
        {
            await sink.WriteAsync(point, cancellationToken);
        }

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
