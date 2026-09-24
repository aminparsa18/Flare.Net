using Flare.Ingest.Stats;
using Google.Protobuf;
using Google.Protobuf.Reflection;
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Status = Google.Rpc.Status;

namespace Flare.Ingest.Auth;

/// <summary>
/// Writes the OTLP-spec throttling response for a key over its ingestion limit (ADR-0051),
/// in the form every conforming exporter treats as "retry later", not "drop":
/// <list type="bullet">
/// <item>OTLP/HTTP: <c>429 Too Many Requests</c> + <c>Retry-After</c>, with the body the
/// spec requires for any 4xx - a <c>google.rpc.Status</c>, protobuf- or JSON-encoded to
/// match the request.</item>
/// <item>OTLP/gRPC: a trailers-only <c>RESOURCE_EXHAUSTED</c> carrying a <c>RetryInfo</c>
/// detail - the spec only treats <c>RESOURCE_EXHAUSTED</c> as retryable when that detail is
/// present.</item>
/// </list>
/// </summary>
/// <remarks>
/// Unlike the middleware's bare 401 (see its remarks), this one builds a real gRPC status
/// from raw middleware: here the distinction is operationally meaningful, since a bare
/// HTTP error makes gRPC exporters drop the batch instead of backing off. A trailers-only
/// response is just <c>grpc-status</c>/<c>grpc-message</c>/<c>grpc-status-details-bin</c>
/// as HTTP/2 headers on a bodiless 200 - the same shape <c>Grpc.AspNetCore</c> itself
/// emits for a call that fails before writing any message.
/// </remarks>
public static class IngestKeyLimitRejection
{
    public const string Message = "Ingest key limit exceeded";

    private static readonly JsonFormatter StatusJsonFormatter =
        new(JsonFormatter.Settings.Default.WithTypeRegistry(TypeRegistry.FromMessages(RetryInfo.Descriptor)));

    public static async Task WriteAsync(HttpContext context, TimeSpan retryAfter)
    {
        var retrySeconds = Math.Max(1, (long)Math.Ceiling(retryAfter.TotalSeconds));
        var response = context.Response;

        if (IsGrpc(context.Request))
        {
            var status = BuildStatus((int)Grpc.Core.StatusCode.ResourceExhausted, retrySeconds);
            response.StatusCode = StatusCodes.Status200OK;
            response.ContentType = "application/grpc";
            response.Headers["grpc-status"] = ((int)Grpc.Core.StatusCode.ResourceExhausted).ToString();
            response.Headers["grpc-message"] = Message;
            response.Headers["grpc-status-details-bin"] = Convert.ToBase64String(status.ToByteArray());
            return;
        }

        // google.rpc.Status.code is a gRPC code even on OTLP/HTTP - RESOURCE_EXHAUSTED
        // mirrors the gRPC branch, the HTTP status line carries the 429.
        var body = BuildStatus((int)Grpc.Core.StatusCode.ResourceExhausted, retrySeconds);
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        response.Headers.RetryAfter = retrySeconds.ToString();

        if (context.Request.ContentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) ?? false)
        {
            response.ContentType = "application/json";
            await response.WriteAsync(StatusJsonFormatter.Format(body));
        }
        else
        {
            response.ContentType = "application/x-protobuf";
            await response.Body.WriteAsync(body.ToByteArray());
        }
    }

    /// <summary>Which signal a rejected OTLP request was for, so the rejection still shows
    /// up per signal on the Ingestion page - by <c>/v1/{signal}</c> path for HTTP, by
    /// proto service name for gRPC. Null for anything unrecognized.</summary>
    public static IngestionSignal? SignalOf(HttpRequest request)
    {
        var path = request.Path.Value ?? string.Empty;
        if (path.StartsWith("/v1/logs", StringComparison.Ordinal) || path.Contains(".logs.v1.", StringComparison.Ordinal))
        {
            return IngestionSignal.Logs;
        }
        if (path.StartsWith("/v1/traces", StringComparison.Ordinal) || path.Contains(".trace.v1.", StringComparison.Ordinal))
        {
            return IngestionSignal.Traces;
        }
        if (path.StartsWith("/v1/metrics", StringComparison.Ordinal) || path.Contains(".metrics.v1.", StringComparison.Ordinal))
        {
            return IngestionSignal.Metrics;
        }
        return null;
    }

    public static bool IsGrpc(HttpRequest request) =>
        request.ContentType?.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase) ?? false;

    private static Status BuildStatus(int code, long retrySeconds) => new()
    {
        Code = code,
        Message = Message,
        Details = { Any.Pack(new RetryInfo { RetryDelay = Duration.FromTimeSpan(TimeSpan.FromSeconds(retrySeconds)) }) },
    };
}
