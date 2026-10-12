namespace Flare.Api.Auditing;

/// <summary>Bound from the <c>Audit</c> section (<c>Audit__RetentionDays</c>).</summary>
public sealed class AuditOptions
{
    public const string SectionName = "Audit";

    /// <summary>Audit events older than this are deleted (checked hourly). 0 or negative keeps them forever.</summary>
    public int RetentionDays { get; set; } = 365;

    /// <summary>Also record who queried telemetry (logs, traces, metrics, errors) - ADR-0204. Off by default.</summary>
    public bool RecordReads { get; set; }

    /// <summary>
    /// A user repeating the same read (dashboards auto-refresh) is recorded once per this many seconds.
    /// 0 records every request.
    /// </summary>
    public int ReadDedupeSeconds { get; set; } = 60;

    /// <summary>Optional forwarding of every new event to external sinks (ADR-0203).</summary>
    public AuditForwardingOptions Forwarding { get; set; } = new();
}

public sealed class AuditForwardingOptions
{
    /// <summary>How often each sink polls for new events.</summary>
    public int IntervalSeconds { get; set; } = 10;

    /// <summary>Events per delivery.</summary>
    public int BatchSize { get; set; } = 100;

    public AuditWebhookSinkOptions Webhook { get; set; } = new();

    public AuditSyslogSinkOptions Syslog { get; set; } = new();
}

public sealed class AuditWebhookSinkOptions
{
    /// <summary>POST target for a JSON array of events. Empty disables the sink.</summary>
    public string? Url { get; set; }

    /// <summary>When set, each request carries <c>X-Flare-Signature: sha256=&lt;hmac of the body&gt;</c>.</summary>
    public string? Secret { get; set; }
}

public sealed class AuditSyslogSinkOptions
{
    /// <summary>Syslog receiver host. Empty disables the sink.</summary>
    public string? Host { get; set; }

    public int Port { get; set; } = 514;

    /// <summary><c>tcp</c> (RFC 6587 octet counting, default) or <c>udp</c>.</summary>
    public string Protocol { get; set; } = "tcp";
}
