using Flare.Api.Alerting;
using Flare.Api.Model;

namespace Flare.Api.Ai;

/// <summary>A group of similar log lines (Drain pattern, or the body when unclustered) seen in the alert window.</summary>
public sealed record IncidentLogPattern(string Template, string Service, byte SeverityNumber, ulong Count, string TraceId);

/// <summary>An exception type/message seen in the alert window and how often.</summary>
public sealed record IncidentExceptionGroup(string Type, string Message, ulong Count, string Service, string TraceId);

/// <summary>An error span of the representative trace.</summary>
public sealed record IncidentErrorSpan(string Service, string Name, string StatusMessage, double DurationMs);

/// <summary>
/// Everything about one fired alert that goes into the incident-summary prompt (ADR-0104): the rule
/// and what it observed, the previous window for comparison, and a bounded sample of the evidence
/// behind it. Raw, unredacted - <see cref="IncidentSummaryPromptBuilder"/> redacts and bounds it.
/// </summary>
public sealed record IncidentEvidence
{
    public required AlertRule Rule { get; init; }

    public required DateTimeOffset From { get; init; }

    public required DateTimeOffset To { get; init; }

    public bool NoData { get; init; }

    /// <summary>Observed count (log/exception rules) or value (metric/anomaly rules).</summary>
    public double Observed { get; init; }

    public string? MetricUnit { get; init; }

    /// <summary>The same measure over the window just before <see cref="From"/>; null when not comparable (no-data, anomaly - which has its own baseline).</summary>
    public double? Previous { get; init; }

    public AnomalyScore? Anomaly { get; init; }

    public IReadOnlyList<IncidentLogPattern> LogPatterns { get; init; } = [];

    public IReadOnlyList<IncidentExceptionGroup> Exceptions { get; init; } = [];

    public IReadOnlyList<IncidentErrorSpan> ErrorSpans { get; init; } = [];
}
