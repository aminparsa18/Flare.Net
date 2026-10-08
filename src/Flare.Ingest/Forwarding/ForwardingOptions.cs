namespace Flare.Ingest.Forwarding;

public enum ForwardingSignal { Logs, Traces, Metrics }

/// <summary>
/// Bound from the <c>Forwarding</c> section (ADR-0155). Empty <see cref="Targets"/> (the default)
/// means forwarding is off and the receivers pay nothing beyond one count check.
/// </summary>
public sealed class ForwardingOptions
{
    public const string SectionName = "Forwarding";

    public List<ForwardingTargetOptions> Targets { get; set; } = [];

    /// <summary>Throws on a target that could never deliver, so a typo fails at startup rather than silently dropping data.</summary>
    public void Validate()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in Targets)
        {
            if (string.IsNullOrWhiteSpace(target.Name) || !names.Add(target.Name))
            {
                throw new InvalidOperationException($"{SectionName}:Targets needs a unique, non-empty Name (got '{target.Name}').");
            }

            if (!Uri.TryCreate(target.Endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException($"{SectionName}:Targets:{target.Name}:Endpoint must be an absolute http(s) URL (got '{target.Endpoint}').");
            }

            if (target.QueueCapacity <= 0 || target.MaxAttempts <= 0 || target.Timeout <= TimeSpan.Zero)
            {
                throw new InvalidOperationException($"{SectionName}:Targets:{target.Name}: QueueCapacity, MaxAttempts and Timeout must be positive.");
            }
        }
    }
}

/// <summary>One OTLP/HTTP (protobuf) destination that receives a copy of accepted telemetry.</summary>
public sealed class ForwardingTargetOptions
{
    public string Name { get; set; } = "";

    /// <summary>Base URL of the receiving OTLP/HTTP endpoint, e.g. <c>https://collector:4318</c>; <c>/v1/logs</c> etc. is appended.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>Extra request headers, e.g. an <c>Authorization</c> for a hosted backend.</summary>
    public Dictionary<string, string> Headers { get; set; } = [];

    /// <summary>Signals to forward; empty = logs, traces and metrics.</summary>
    public List<ForwardingSignal> Signals { get; set; } = [];

    /// <summary>Forward only resources whose <c>service.name</c> is listed; empty = every service.</summary>
    public List<string> Services { get; set; } = [];

    /// <summary>Forward only requests authenticated with one of these ingest keys (ids); empty = any request, including unauthenticated.</summary>
    public List<Guid> IngestKeyIds { get; set; } = [];

    public bool Gzip { get; set; } = true;

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Requests buffered in memory per target; when full the newest are dropped and counted, never blocking ingest.</summary>
    public int QueueCapacity { get; set; } = 1000;

    /// <summary>Delivery attempts (first try included) for network errors, 429 and 5xx.</summary>
    public int MaxAttempts { get; set; } = 3;
}
