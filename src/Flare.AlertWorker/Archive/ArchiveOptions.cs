namespace Flare.AlertWorker.Archive;

/// <summary>
/// Settings for the telemetry archive (ADR-0156), bound from the <c>Archive</c> section. Off by default.
/// </summary>
public sealed class ArchiveOptions
{
    public const string SectionName = "Archive";

    public bool Enabled { get; set; }

    /// <summary>Bucket URL, path-style, e.g. <c>http://rustfs:9000/flare-archive</c>. Objects are written beneath it.</summary>
    public string Endpoint { get; set; } = "";

    public string AccessKey { get; set; } = "";

    public string SecretKey { get; set; } = "";

    /// <summary>Key prefix inside the bucket; blank writes at the bucket root.</summary>
    public string Prefix { get; set; } = "flare";

    /// <summary><c>Parquet</c> or <c>Ndjson</c> (gzip-compressed, one JSON object per line).</summary>
    public ArchiveFormat Format { get; set; } = ArchiveFormat.Parquet;

    /// <summary>Which signals to archive; empty = logs, traces and metrics.</summary>
    public List<ArchiveSignal> Signals { get; set; } = [];

    /// <summary>How often to look for finished hours.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>An hour is exported only once it ended this long ago, so rows still in the ingest buffer land in it first.</summary>
    public TimeSpan Lag { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Where to begin on the first run, by ingest time. Blank = the current hour (no backfill).</summary>
    public DateTimeOffset? StartFrom { get; set; }

    /// <summary>Hours exported per table per poll, so a long backfill cannot monopolise ClickHouse.</summary>
    public int MaxWindowsPerPoll { get; set; } = 6;

    /// <summary>Execution-time cap for one hour's export.</summary>
    public int MaxExecutionSeconds { get; set; } = 600;

    public string? Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return "Archive:Endpoint must be an absolute http(s) bucket URL.";
        }

        if (string.IsNullOrWhiteSpace(AccessKey) || string.IsNullOrWhiteSpace(SecretKey))
        {
            return "Archive:AccessKey and Archive:SecretKey are required.";
        }

        return MaxWindowsPerPoll <= 0 || MaxExecutionSeconds <= 0 || PollInterval <= TimeSpan.Zero
            ? "Archive:MaxWindowsPerPoll, MaxExecutionSeconds and PollInterval must be positive."
            : null;
    }
}

public enum ArchiveFormat { Parquet, Ndjson }

public enum ArchiveSignal { Logs, Traces, Metrics }
