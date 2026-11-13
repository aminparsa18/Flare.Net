using OpenTelemetry.Proto.Collector.Metrics.V1;

namespace Flare.Ingest.Otlp;

/// <summary>
/// Builds the OTLP export response's <c>partial_success</c> field when Ingest accepted a request
/// but did not store all of it, so senders that read the field (the Collector, the Go and Java
/// SDKs) log the loss instead of seeing a plain success.
/// </summary>
/// <remarks>
/// Only data Ingest <em>can't</em> store counts as rejected - today that's metric data points of
/// an unsupported type (Summary). Intentional drops are not rejections and are left out: trace
/// sampling (ADR-0122) and staleness markers (<c>FLAG_NO_RECORDED_VALUE</c>) would otherwise make
/// every export warn about behavior the operator asked for. Ingest-key limits and a disallowed
/// service refuse the whole request (429/403) before anything is stored, so they never produce a
/// partial success. Per the OTLP spec the response stays a success and the sender must not retry.
/// </remarks>
public static class OtlpPartialSuccess
{
    /// <summary>Most metric names listed in <c>error_message</c>; the rest are summarized as a count.</summary>
    public const int MaxNamesInMessage = 10;

    /// <summary>Returns the response for <paramref name="result"/>, with <c>partial_success</c> set
    /// only when at least one data point was rejected.</summary>
    public static ExportMetricsServiceResponse ForMetrics(MetricMapResult result)
    {
        var response = new ExportMetricsServiceResponse();
        if (result.RejectedDataPoints <= 0)
        {
            return response;
        }

        var names = result.UnsupportedMetricNames;
        var listed = string.Join(", ", names.Take(MaxNamesInMessage));
        if (names.Count > MaxNamesInMessage)
        {
            listed += $" and {names.Count - MaxNamesInMessage} more";
        }

        response.PartialSuccess = new ExportMetricsPartialSuccess
        {
            RejectedDataPoints = result.RejectedDataPoints,
            ErrorMessage = $"Summary metrics are not supported; dropped {result.RejectedDataPoints} data point(s) from: {listed}",
        };
        return response;
    }
}
