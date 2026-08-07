using Flare.Ingest.Sinks;
using Grpc.Core;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Flare.Ingest.Otlp;

/// <summary>
/// gRPC OTLP logs receiver - <c>opentelemetry.proto.collector.logs.v1.LogsService/Export</c>,
/// served on the "otlp-grpc" endpoint (conventionally port 4317).
/// </summary>
public sealed class OtlpGrpcLogsService(ILogEventSink sink, ILogger<OtlpGrpcLogsService> logger) : LogsService.LogsServiceBase
{
    public override async Task<ExportLogsServiceResponse> Export(
        ExportLogsServiceRequest request,
        ServerCallContext context)
    {
        var count = 0;
        foreach (var logEvent in OtlpLogMapper.Map(request))
        {
            await sink.WriteAsync(logEvent, context.CancellationToken);
            count++;
        }

        logger.LogDebug("Ingested {Count} log record(s) via gRPC", count);
        return new ExportLogsServiceResponse();
    }
}