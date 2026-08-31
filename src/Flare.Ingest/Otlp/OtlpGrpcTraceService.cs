using Flare.Ingest.Sinks;
using Flare.Ingest.Stats;
using Grpc.Core;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Flare.Ingest.Otlp;

/// <summary>
/// gRPC OTLP traces receiver - <c>opentelemetry.proto.collector.trace.v1.TraceService/Export</c>,
/// served on the "otlp-grpc" endpoint (conventionally port 4317), same shared listener
/// as <see cref="OtlpGrpcLogsService"/>.
/// </summary>
public sealed class OtlpGrpcTraceService(
    ISpanEventSink sink,
    IIngestionStatsTracker stats,
    TimeProvider timeProvider,
    ILogger<OtlpGrpcTraceService> logger) : TraceService.TraceServiceBase
{
    public override async Task<ExportTraceServiceResponse> Export(
        ExportTraceServiceRequest request,
        ServerCallContext context)
    {
        var byteCount = request.CalculateSize();

        // Captured once per request, not per span - see LogEvent.IngestedAt's remarks
        // and ADR-0014 (SpanRecord.IngestedAt follows the same convention).
        var ingestedAt = timeProvider.GetUtcNow();

        int count;
        var records = new List<(string? ServiceName, long SkewNanos)>();
        try
        {
            count = 0;
            foreach (var span in OtlpTraceMapper.Map(request, ingestedAt))
            {
                await sink.WriteAsync(span, context.CancellationToken);
                count++;
                records.Add((span.ServiceName, ClockSkew.Nanos(ingestedAt, span.StartTime)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not RpcException)
        {
            logger.LogWarning(ex, "Rejected malformed OTLP traces export via gRPC");
            await stats.RecordRejectedAsync(IngestionSignal.Traces, IngestionProtocol.Grpc, $"invalid-payload:{ex.GetType().Name}", context.CancellationToken);
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Malformed traces export"));
        }

        await stats.RecordAcceptedAsync(IngestionSignal.Traces, IngestionProtocol.Grpc, count, byteCount, context.CancellationToken);
        await stats.RecordServiceBreakdownAsync(IngestionSignal.Traces, ServiceBreakdown.Build(records, byteCount), context.CancellationToken);

        logger.LogDebug("Ingested {Count} span(s) via gRPC", count);
        return new ExportTraceServiceResponse();
    }
}
