using Flare.Ingest.Sinks;
using Flare.Ingest.Stats;
using Grpc.Core;
using OpenTelemetry.Proto.Collector.Logs.V1;

namespace Flare.Ingest.Otlp;

/// <summary>
/// gRPC OTLP logs receiver - <c>opentelemetry.proto.collector.logs.v1.LogsService/Export</c>,
/// served on the "otlp-grpc" endpoint (conventionally port 4317).
/// </summary>
public sealed class OtlpGrpcLogsService(
    ILogEventSink sink,
    IIngestionStatsTracker stats,
    ILogger<OtlpGrpcLogsService> logger) : LogsService.LogsServiceBase
{
    public override async Task<ExportLogsServiceResponse> Export(
        ExportLogsServiceRequest request,
        ServerCallContext context)
    {
        // Unlike the HTTP endpoints, gRPC has already deserialized the wire message by the
        // time this method runs - CalculateSize() re-derives the same protobuf byte length
        // for stats purposes without needing the original raw bytes.
        var byteCount = request.CalculateSize();

        int count;
        try
        {
            count = 0;
            foreach (var logEvent in OtlpLogMapper.Map(request))
            {
                await sink.WriteAsync(logEvent, context.CancellationToken);
                count++;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not RpcException)
        {
            logger.LogWarning(ex, "Rejected malformed OTLP logs export via gRPC");
            await stats.RecordRejectedAsync(IngestionSignal.Logs, IngestionProtocol.Grpc, $"invalid-payload:{ex.GetType().Name}", context.CancellationToken);
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Malformed logs export"));
        }

        await stats.RecordAcceptedAsync(IngestionSignal.Logs, IngestionProtocol.Grpc, count, byteCount, context.CancellationToken);

        logger.LogDebug("Ingested {Count} log record(s) via gRPC", count);
        return new ExportLogsServiceResponse();
    }
}
