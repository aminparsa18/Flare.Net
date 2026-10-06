using Flare.Ingest.Auth;
using Flare.Ingest.Sinks;
using Flare.Ingest.Stats;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Profiles.V1Development;

namespace Flare.Ingest.Otlp;

/// <summary>
/// HTTP OTLP profiles receiver - <c>POST /v1development/profiles</c> (Alpha signal, ADR-0141; the OTLP/HTTP
/// path carries the proto package version), on the "otlp-http" endpoint (conventionally port 4318).
/// Content-negotiates protobuf and JSON, same shape as <see cref="OtlpHttpLogsEndpoints"/>.
/// </summary>
public static class OtlpHttpProfilesEndpoints
{
    private const string ProtobufContentType = "application/x-protobuf";
    private const string JsonContentType = "application/json";

    public static IEndpointRouteBuilder MapOtlpHttpProfilesEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/v1development/profiles", HandleExportAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleExportAsync(
        HttpContext http,
        IProfileSampleSink sink,
        IIngestionStatsTracker stats,
        TimeProvider timeProvider,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        // OtlpHttpProfilesEndpoints is a static class (it's an extension-method container,
        // not a service), so it can't be used as ILogger<T>'s type argument - a named
        // category via ILoggerFactory is the standard way to get a scoped logger here,
        // same as OtlpHttpLogsEndpoints.
        var logger = loggerFactory.CreateLogger("Flare.Ingest.Otlp.OtlpHttpProfilesEndpoints");

        var contentType = http.Request.ContentType ?? string.Empty;
        var isJson = contentType.Contains(JsonContentType, StringComparison.OrdinalIgnoreCase);
        var isProtobuf = contentType.Contains(ProtobufContentType, StringComparison.OrdinalIgnoreCase);

        if (!isJson && !isProtobuf && contentType.Length > 0)
        {
            await stats.RecordRejectedAsync(IngestionSignal.Profiles, IngestionProtocol.Http, "unsupported-media-type", cancellationToken);
            return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);
        }

        ExportProfilesServiceRequest request;
        long byteCount;
        try
        {
            if (isJson)
            {
                using var buffer = new MemoryStream();
                await http.Request.Body.CopyToAsync(buffer, cancellationToken);
                byteCount = buffer.Length;
                request = OtlpJson.Parse<ExportProfilesServiceRequest>(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
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
                request = ExportProfilesServiceRequest.Parser.ParseFrom(buffer);
            }
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            // Body exceeded Otlp:MaxRequestSizeBytes (Kestrel's MaxRequestBodySize, set in
            // Program.cs). 413, not 400: it's a size limit, not a malformed payload.
            await stats.RecordRejectedAsync(IngestionSignal.Profiles, IngestionProtocol.Http, "payload-too-large", cancellationToken);
            return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Rejected malformed OTLP profiles export via HTTP ({ContentType})", isJson ? "json" : "protobuf");
            await stats.RecordRejectedAsync(IngestionSignal.Profiles, IngestionProtocol.Http, $"invalid-payload:{ex.GetType().Name}", cancellationToken);
            return Results.BadRequest();
        }

        // Captured once per request, not per sample - see LogEvent.IngestedAt's remarks
        // and ADR-0014.
        var ingestedAt = timeProvider.GetUtcNow();

        var count = 0;
        var records = new List<(string? ServiceName, long SkewNanos)>();
        foreach (var sample in OtlpProfilesMapper.Map(request, ingestedAt))
        {
            await sink.WriteAsync(sample, cancellationToken);
            count++;
            records.Add((sample.ServiceName, ClockSkew.Nanos(ingestedAt, sample.Timestamp)));
        }

        await stats.RecordAcceptedAsync(IngestionSignal.Profiles, IngestionProtocol.Http, count, byteCount, cancellationToken);
        IngestKeyUsageFeature.Add(http, count, byteCount);
        await stats.RecordServiceBreakdownAsync(IngestionSignal.Profiles, ServiceBreakdown.Build(records, byteCount), cancellationToken);

        logger.LogDebug("Ingested {Count} profile sample(s) via HTTP ({ContentType})", count, isJson ? "json" : "protobuf");

        var response = new ExportProfilesServiceResponse();
        return isJson
            ? Results.Text(JsonFormatter.Default.Format(response), JsonContentType)
            : Results.Bytes(response.ToByteArray(), ProtobufContentType);
    }
}
