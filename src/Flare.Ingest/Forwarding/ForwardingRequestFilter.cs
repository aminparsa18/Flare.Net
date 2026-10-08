using Flare.Ingest.Otlp;
using Google.Protobuf.Collections;
using OpenTelemetry.Proto.Collector.Logs.V1;
using OpenTelemetry.Proto.Collector.Metrics.V1;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Resource.V1;

namespace Flare.Ingest.Forwarding;

/// <summary>
/// Narrows an export to the resources whose <c>service.name</c> a target lists. Pure, so it is
/// unit-tested. An empty set returns the request untouched; no match returns null (nothing to send).
/// </summary>
public static class ForwardingRequestFilter
{
    public static ExportLogsServiceRequest? Filter(ExportLogsServiceRequest request, IReadOnlySet<string> services)
    {
        if (services.Count == 0) return request;
        var kept = request.ResourceLogs.Where(r => Matches(r.Resource, services)).ToList();
        if (kept.Count == 0) return null;
        var copy = new ExportLogsServiceRequest();
        copy.ResourceLogs.AddRange(kept);
        return copy;
    }

    public static ExportTraceServiceRequest? Filter(ExportTraceServiceRequest request, IReadOnlySet<string> services)
    {
        if (services.Count == 0) return request;
        var kept = request.ResourceSpans.Where(r => Matches(r.Resource, services)).ToList();
        if (kept.Count == 0) return null;
        var copy = new ExportTraceServiceRequest();
        copy.ResourceSpans.AddRange(kept);
        return copy;
    }

    public static ExportMetricsServiceRequest? Filter(ExportMetricsServiceRequest request, IReadOnlySet<string> services)
    {
        if (services.Count == 0) return request;
        var kept = request.ResourceMetrics.Where(r => Matches(r.Resource, services)).ToList();
        if (kept.Count == 0) return null;
        var copy = new ExportMetricsServiceRequest();
        copy.ResourceMetrics.AddRange(kept);
        return copy;
    }

    private static bool Matches(Resource? resource, IReadOnlySet<string> services) =>
        OtlpAnyValue.Flatten(resource?.Attributes).GetValueOrDefault("service.name") is { } name && services.Contains(name);
}
