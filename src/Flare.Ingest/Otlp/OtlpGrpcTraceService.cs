using Flare.Ingest.Sinks;
using Grpc.Core;
using OpenTelemetry.Proto.Collector.Trace.V1;

namespace Flare.Ingest.Otlp;

/// <summary>
/// gRPC OTLP traces receiver - <c>opentelemetry.proto.collector.trace.v1.TraceService/Export</c>,
/// served on the "otlp-grpc" endpoint (conventionally port 4317), same shared listener
/// as <see cref="OtlpGrpcLogsService"/>.
/// </summary>
public sealed class OtlpGrpcTraceService(ISpanEventSink sink, ILogger<OtlpGrpcTraceService> logger) : TraceService.TraceServiceBase
{
    public override async Task<ExportTraceServiceResponse> Export(
        ExportTraceServiceRequest request,
        ServerCallContext context)
    {
        var count = 0;
        foreach (var span in OtlpTraceMapper.Map(request))
        {
            await sink.WriteAsync(span, context.CancellationToken);
            count++;
        }

        logger.LogDebug("Ingested {Count} span(s) via gRPC", count);
        return new ExportTraceServiceResponse();
    }
}
