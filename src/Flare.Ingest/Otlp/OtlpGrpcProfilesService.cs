using Flare.Ingest.Auth;
using Flare.Ingest.Sinks;
using Flare.Ingest.Stats;
using Grpc.Core;
using OpenTelemetry.Proto.Collector.Profiles.V1Development;

namespace Flare.Ingest.Otlp;

/// <summary>
/// gRPC OTLP profiles receiver - <c>opentelemetry.proto.collector.profiles.v1development.ProfilesService/Export</c>
/// (Alpha signal, ADR-0141), served on the shared "otlp-grpc" endpoint like <see cref="OtlpGrpcTraceService"/>.
/// </summary>
public sealed class OtlpGrpcProfilesService(
    IProfileSampleSink sink,
    IIngestionStatsTracker stats,
    TimeProvider timeProvider,
    ILogger<OtlpGrpcProfilesService> logger) : ProfilesService.ProfilesServiceBase
{
    public override async Task<ExportProfilesServiceResponse> Export(
        ExportProfilesServiceRequest request,
        ServerCallContext context)
    {
        var byteCount = request.CalculateSize();

        if (IngestKeyScope.RejectsServices(context.GetHttpContext(), request.ResourceProfiles.Select(r => r.Resource)))
        {
            await stats.RecordRejectedAsync(IngestionSignal.Profiles, IngestionProtocol.Grpc, "service-not-allowed", context.CancellationToken);
            throw new RpcException(new Status(StatusCode.PermissionDenied, "Service not allowed for this ingest key"));
        }
        var ingestedAt = timeProvider.GetUtcNow();

        int count;
        var records = new List<(string? ServiceName, long SkewNanos)>();
        try
        {
            count = 0;
            foreach (var sample in OtlpProfilesMapper.Map(request, ingestedAt))
            {
                await sink.WriteAsync(sample, context.CancellationToken);
                count++;
                records.Add((sample.ServiceName, ClockSkew.Nanos(ingestedAt, sample.Timestamp)));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not RpcException)
        {
            logger.LogWarning(ex, "Rejected malformed OTLP profiles export via gRPC");
            await stats.RecordRejectedAsync(IngestionSignal.Profiles, IngestionProtocol.Grpc, $"invalid-payload:{ex.GetType().Name}", context.CancellationToken);
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Malformed profiles export"));
        }

        await stats.RecordAcceptedAsync(IngestionSignal.Profiles, IngestionProtocol.Grpc, count, byteCount, context.CancellationToken);
        IngestKeyUsageFeature.Add(context.GetHttpContext(), count, byteCount);
        await stats.RecordServiceBreakdownAsync(IngestionSignal.Profiles, ServiceBreakdown.Build(records, byteCount), context.CancellationToken);

        logger.LogDebug("Ingested {Count} profile sample(s) via gRPC", count);
        return new ExportProfilesServiceResponse();
    }
}
