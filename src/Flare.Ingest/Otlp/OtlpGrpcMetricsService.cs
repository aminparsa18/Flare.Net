using Flare.Ingest.Sinks;
using Flare.Ingest.Stats;
using Grpc.Core;
using OpenTelemetry.Proto.Collector.Metrics.V1;

namespace Flare.Ingest.Otlp;

/// <summary>
/// gRPC OTLP metrics receiver - <c>opentelemetry.proto.collector.metrics.v1.MetricsService/Export</c>,
/// served on the "otlp-grpc" endpoint (conventionally port 4317), same shared listener
/// as <see cref="OtlpGrpcTraceService"/>.
/// </summary>
public sealed class OtlpGrpcMetricsService(
    IMetricEventSink sink,
    IIngestionStatsTracker stats,
    TimeProvider timeProvider,
    ILogger<OtlpGrpcMetricsService> logger) : MetricsService.MetricsServiceBase
{
    public override async Task<ExportMetricsServiceResponse> Export(
        ExportMetricsServiceRequest request,
        ServerCallContext context)
    {
        var byteCount = request.CalculateSize();

        // Captured once per request, not per data point - see LogEvent.IngestedAt's
        // remarks and ADR-0014.
        var ingestedAt = timeProvider.GetUtcNow();

        MetricMapResult result;
        try
        {
            result = OtlpMetricsMapper.Map(request, ingestedAt);
            foreach (var point in result.Points)
            {
                await sink.WriteAsync(point, context.CancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not RpcException)
        {
            logger.LogWarning(ex, "Rejected malformed OTLP metrics export via gRPC");
            await stats.RecordRejectedAsync(IngestionSignal.Metrics, IngestionProtocol.Grpc, $"invalid-payload:{ex.GetType().Name}", context.CancellationToken);
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Malformed metrics export"));
        }

        await stats.RecordAcceptedAsync(IngestionSignal.Metrics, IngestionProtocol.Grpc, result.Points.Count, byteCount, context.CancellationToken);
        await stats.RecordServiceBreakdownAsync(
            IngestionSignal.Metrics,
            ServiceBreakdown.Build(result.Points.Select(p => (p.ServiceName, ClockSkew.Nanos(ingestedAt, p.Time))), byteCount),
            context.CancellationToken);

        if (result.UnsupportedMetricNames.Count > 0)
        {
            logger.LogWarning(
                "Dropped data points for {Count} metric(s) with an unsupported point type (ExponentialHistogram/Summary not yet supported): {Names}",
                result.UnsupportedMetricNames.Count,
                string.Join(", ", result.UnsupportedMetricNames));
        }

        logger.LogDebug("Ingested {Count} metric data point(s) via gRPC", result.Points.Count);
        return new ExportMetricsServiceResponse();
    }
}
