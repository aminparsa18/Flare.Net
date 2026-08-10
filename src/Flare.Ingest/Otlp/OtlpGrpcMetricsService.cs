using Flare.Ingest.Sinks;
using Grpc.Core;
using OpenTelemetry.Proto.Collector.Metrics.V1;

namespace Flare.Ingest.Otlp;

/// <summary>
/// gRPC OTLP metrics receiver - <c>opentelemetry.proto.collector.metrics.v1.MetricsService/Export</c>,
/// served on the "otlp-grpc" endpoint (conventionally port 4317), same shared listener
/// as <see cref="OtlpGrpcTraceService"/>.
/// </summary>
public sealed class OtlpGrpcMetricsService(IMetricEventSink sink, ILogger<OtlpGrpcMetricsService> logger) : MetricsService.MetricsServiceBase
{
    public override async Task<ExportMetricsServiceResponse> Export(
        ExportMetricsServiceRequest request,
        ServerCallContext context)
    {
        var result = OtlpMetricsMapper.Map(request);
        foreach (var point in result.Points)
        {
            await sink.WriteAsync(point, context.CancellationToken);
        }

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
