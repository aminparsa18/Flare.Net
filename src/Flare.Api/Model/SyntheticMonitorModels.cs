using Flare.Api.Synthetic;

namespace Flare.Api.Model;

/// <summary>What a <see cref="SyntheticMonitor"/> probes.</summary>
public enum SyntheticMonitorKind
{
    /// <summary>An HTTP(S) request; up when the status matches <see cref="SyntheticMonitor.ExpectedStatus"/>.</summary>
    Http,

    /// <summary>A TCP connect to <c>host:port</c>.</summary>
    Tcp,

    /// <summary>A TLS handshake to <c>host:port</c>; also reports days until the certificate expires.</summary>
    Tls,
}

/// <summary>
/// A scheduled probe. <c>Flare.AlertWorker</c> runs it every <see cref="IntervalSeconds"/> and writes the
/// result as gauge metrics (<see cref="SyntheticMetrics"/>). JSON only - not MemoryPack'd. See
/// <c>docs-internal/adr/0128-synthetic-monitoring.md</c>.
/// </summary>
public sealed record SyntheticMonitor
{
    public required Guid Id { get; init; }

    public required string Name { get; init; }

    public string Description { get; init; } = "";

    public bool Enabled { get; init; } = true;

    public SyntheticMonitorKind Kind { get; init; } = SyntheticMonitorKind.Http;

    /// <summary>Http: an absolute http(s) URL. Tcp/Tls: <c>host:port</c> (Tls defaults to 443).</summary>
    public required string Target { get; init; }

    /// <summary>Http only.</summary>
    public string Method { get; init; } = "GET";

    /// <summary>Http only: the status that counts as up. 0 means any 2xx or 3xx.</summary>
    public int ExpectedStatus { get; init; }

    /// <summary>Http only: one <c>Name: value</c> header per line. Empty means none.</summary>
    public string RequestHeaders { get; init; } = "";

    /// <summary>Http POST only. Empty means no body.</summary>
    public string RequestBody { get; init; } = "";

    /// <summary>Http only: the response body must contain this text. Empty means no assertion.</summary>
    public string BodyContains { get; init; } = "";

    /// <summary>Http only: the response body must not contain this text. Empty means no assertion.</summary>
    public string BodyNotContains { get; init; } = "";

    public int IntervalSeconds { get; init; } = 60;

    public int TimeoutSeconds { get; init; } = 10;

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    /// <summary>The most recent probe result. Filled only by the list endpoint; null when no result has been recorded yet.</summary>
    public SyntheticMonitorStatus? Latest { get; init; }
}

/// <summary>The most recent probe result of a monitor, read back from its <see cref="SyntheticMetrics"/> points.</summary>
public sealed record SyntheticMonitorStatus(
    bool Up,
    DateTimeOffset Time,
    double? DurationMs,
    int? HttpStatus,
    double? CertExpiryDays)
{
    /// <summary>One stored point: a metric name, its timestamp and its value.</summary>
    public readonly record struct Point(string MetricName, DateTimeOffset Time, double Value);

    /// <summary>
    /// Builds the status from one monitor's latest point per metric. The <see cref="SyntheticMetrics.Up"/> point
    /// anchors the probe: the other metrics count only when written by that same probe, so a failed probe (which
    /// writes no status code or certificate) never shows an older probe's values. Null when there is no up point.
    /// </summary>
    public static SyntheticMonitorStatus? FromPoints(IReadOnlyCollection<Point> points)
    {
        var up = points.Where(p => p.MetricName == SyntheticMetrics.Up).Select(p => (Point?)p).FirstOrDefault();
        if (up is null)
        {
            return null;
        }

        double? Same(string name) => points.Where(p => p.MetricName == name && p.Time == up.Value.Time).Select(p => (double?)p.Value).FirstOrDefault();
        return new SyntheticMonitorStatus(
            up.Value.Value >= 0.5,
            up.Value.Time,
            Same(SyntheticMetrics.Duration),
            Same(SyntheticMetrics.HttpStatusCode) is { } status ? (int)status : null,
            Same(SyntheticMetrics.CertExpiryDays));
    }
}

/// <summary>Create/update body for <c>/api/synthetic-monitors</c>.</summary>
public sealed record SyntheticMonitorRequest
{
    public const int MaxNameLength = 200;
    public const int MaxTargetLength = 2000;
    public const int MinIntervalSeconds = 10;
    public const int MaxIntervalSeconds = 86_400;
    public const int MaxTimeoutSeconds = 120;
    public const int MaxRequestBodyLength = 65_536;
    public const int MaxAssertionLength = 1_000;

    private static readonly string[] AllowedMethods = ["GET", "HEAD", "POST", "OPTIONS"];

    public required string Name { get; init; }

    public string? Description { get; init; }

    public bool? Enabled { get; init; }

    public SyntheticMonitorKind? Kind { get; init; }

    public required string Target { get; init; }

    public string? Method { get; init; }

    public int? ExpectedStatus { get; init; }

    public string? RequestHeaders { get; init; }

    public string? RequestBody { get; init; }

    public string? BodyContains { get; init; }

    public string? BodyNotContains { get; init; }

    public int? IntervalSeconds { get; init; }

    public int? TimeoutSeconds { get; init; }

    /// <summary>Returns an error message, or null when this request is valid.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return "name is required.";
        }

        if (Name.Length > MaxNameLength)
        {
            return $"name must be at most {MaxNameLength} characters.";
        }

        var kind = Kind ?? SyntheticMonitorKind.Http;
        if (string.IsNullOrWhiteSpace(Target) || Target.Length > MaxTargetLength)
        {
            return $"target is required and must be at most {MaxTargetLength} characters.";
        }

        if (kind == SyntheticMonitorKind.Http)
        {
            if (!Uri.TryCreate(Target.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                return "target must be an absolute http or https URL.";
            }

            if (Method is { } method && !AllowedMethods.Contains(method.ToUpperInvariant()))
            {
                return $"method must be one of {string.Join(", ", AllowedMethods)}.";
            }

            if (ExpectedStatus is < 0 or > 599 or (> 0 and < 100))
            {
                return "expectedStatus must be 0 (any 2xx/3xx) or an HTTP status code.";
            }

            if (SyntheticHeaders.Parse(RequestHeaders) is { Error: { } headerError })
            {
                return headerError;
            }

            if (!string.IsNullOrEmpty(RequestBody))
            {
                if (RequestBody.Length > MaxRequestBodyLength)
                {
                    return $"requestBody must be at most {MaxRequestBodyLength} characters.";
                }

                if (!string.Equals(Method ?? "GET", "POST", StringComparison.OrdinalIgnoreCase))
                {
                    return "requestBody can only be sent with method POST.";
                }
            }

            if ((BodyContains?.Length ?? 0) > MaxAssertionLength || (BodyNotContains?.Length ?? 0) > MaxAssertionLength)
            {
                return $"bodyContains and bodyNotContains must be at most {MaxAssertionLength} characters.";
            }
        }
        else if (SyntheticTarget.ParseHostPort(Target, kind == SyntheticMonitorKind.Tls ? 443 : null) is null)
        {
            return kind == SyntheticMonitorKind.Tls
                ? "target must be host or host:port."
                : "target must be host:port.";
        }

        if (IntervalSeconds is { } interval && (interval < MinIntervalSeconds || interval > MaxIntervalSeconds))
        {
            return $"intervalSeconds must be between {MinIntervalSeconds} and {MaxIntervalSeconds}.";
        }

        if (TimeoutSeconds is { } timeout && (timeout < 1 || timeout > MaxTimeoutSeconds))
        {
            return $"timeoutSeconds must be between 1 and {MaxTimeoutSeconds}.";
        }

        return (TimeoutSeconds ?? 10) > (IntervalSeconds ?? 60) ? "timeoutSeconds must not exceed intervalSeconds." : null;
    }
}

/// <summary>Response body for <c>GET /api/synthetic-monitors</c>.</summary>
public sealed record SyntheticMonitorListResponse(IReadOnlyList<SyntheticMonitor> Monitors);

/// <summary>
/// Names and attribute keys of the gauge metrics a probe writes. Every point carries
/// <see cref="MonitorAttribute"/> (the monitor name), <see cref="KindAttribute"/> and <see cref="TargetAttribute"/>;
/// the service name is <see cref="ServiceName"/>.
/// </summary>
public static class SyntheticMetrics
{
    public const string ServiceName = "flare-synthetic";
    public const string Up = "synthetic.up";
    public const string Duration = "synthetic.duration";
    public const string CertExpiryDays = "synthetic.cert.expiry_days";
    public const string HttpStatusCode = "synthetic.http.status_code";

    public const string MonitorAttribute = "monitor";
    public const string KindAttribute = "kind";
    public const string TargetAttribute = "target";
}
