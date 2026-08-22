using System.ComponentModel;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Flare.Cli.Internal;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Flare.Cli.Commands;

/// <summary>
/// The CLI-native equivalent of the dashboard's Ingestion page - same shape
/// <see cref="MetricsCommand"/>/<see cref="TracesCommand"/> already establish for their own
/// pages. Calls the same two endpoints the dashboard does (<c>GET /api/ingestion/stats</c>,
/// <c>GET /api/ingestion/pipeline</c>) and re-derives the same verdict/status vocabulary the
/// dashboard's <c>ingestion/health.ts</c> computes client-side - a hand-port, not a shared
/// reference (same "different deployables, no reference between them" boundary every other
/// Flare.Api client in this repo draws), kept in sync by hand. Thresholds are copied
/// verbatim from health.ts so `flare ingestion` and the dashboard's own status dot never
/// quietly disagree about what "healthy" means.
///
/// Deliberately narrower than the dashboard page: no chart, no per-service breakdown, no
/// rejected-payload reason drill-down, no topology diagram - those are either inherently
/// visual (the chart, the diagram) or a click-through detail view (the drill-down, the
/// service breakdown) that a one-shot terminal summary isn't the right shape for. This
/// covers the "is ingestion healthy, right now" question in one glance, the same job
/// `flare status`/`flare doctor` already do for the stack's own container health.
/// </summary>
internal sealed class IngestionCommand : AsyncCommand<IngestionCommand.Settings>
{
    private const int DownConsecutiveErrors = 3;
    private const int WarnUtilizationPercent = 70;
    private const int DownUtilizationPercent = 90;
    private const double StuckBacklogAgeSeconds = 5 * 60;
    private const double FlushStaleAgeSeconds = 60;
    private const int RecentActivityLookbackMinutes = 3;

    internal sealed class Settings : CommandSettings
    {
        [CommandOption("--since <RANGE>")]
        [Description("Window for rates/rejected totals and pipeline staleness: 15m, 1h, 6h, 24h. Default 1h.")]
        public string Since { get; init; } = "1h";
    }

    protected override async Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        if (!FlareHome.IsInitialized)
        {
            AnsiConsole.MarkupLine("[grey]Not initialized yet - run `flare start` first.[/]");
            return 1;
        }

        if (!TracesCommand.TryParseSince(settings.Since, out var since))
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't parse --since '{Markup.Escape(settings.Since)}' - expected e.g. 15m, 1h, 6h, 24h.");
            return 1;
        }

        var minutes = Math.Clamp((int)since.TotalMinutes, 1, 1440);

        var port = FlareHome.ReadEnvValue("FLARE_API_PORT", "8080");
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };

        IngestionStatsResponseWire? stats;
        PipelineStatsResponseWire? pipeline;
        try
        {
            var statsTask = http.GetFromJsonAsync<IngestionStatsResponseWire>(
                $"/api/ingestion/stats?minutes={minutes}", IngestionWireJsonOptions.Instance, cancellationToken);
            var pipelineTask = http.GetFromJsonAsync<PipelineStatsResponseWire>(
                $"/api/ingestion/pipeline?minutes={minutes}", IngestionWireJsonOptions.Instance, cancellationToken);
            await Task.WhenAll(statsTask, pipelineTask);
            stats = await statsTask;
            pipeline = await pipelineTask;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            AnsiConsole.MarkupLine($"[red]✗[/] Couldn't reach the API on localhost:{port} - is `api` running? Check `flare status`.");
            return 1;
        }

        if (stats is null || pipeline is null)
        {
            AnsiConsole.MarkupLine("[red]✗[/] Empty response from the API.");
            return 1;
        }

        var verdict = ComputeVerdict(stats, pipeline);
        PrintVerdict(verdict);
        AnsiConsole.WriteLine();

        PrintRates(stats);
        AnsiConsole.WriteLine();

        PrintReceivers(stats);
        AnsiConsole.WriteLine();

        PrintPipeline(stats, pipeline);

        return verdict.Level == "down" ? 1 : 0;
    }

    // ---- Verdict - hand-port of ingestion/health.ts's computeIngestionHealth. ----

    private sealed record Verdict(string Level, string Label, string Detail);

    private static Verdict ComputeVerdict(IngestionStatsResponseWire stats, PipelineStatsResponseWire pipeline)
    {
        var totals = stats.Totals;
        var rejectionRate = totals.RequestsInWindow > 0 ? (double)totals.RejectedInWindow / totals.RequestsInWindow : 0;

        var downReasons = new List<string>();
        var degradedReasons = new List<string>();
        var workersDown = new HashSet<string>();
        var workersStale = new HashSet<string>();

        foreach (var worker in pipeline.FlushWorkers)
        {
            if (worker.ConsecutiveErrors >= DownConsecutiveErrors)
            {
                workersDown.Add(worker.Signal);
                downReasons.Add($"{worker.Signal} exporter unavailable");
                continue;
            }

            if (worker.ConsecutiveErrors > 0)
            {
                degradedReasons.Add($"{worker.Signal} flush retrying ({worker.ConsecutiveErrors} errors)");
                continue;
            }

            if (HasRecentArrivals(stats.Buckets, worker.Signal))
            {
                var ageSeconds = worker.LastFlushAt is { } at ? (DateTimeOffset.UtcNow - at).TotalSeconds : double.PositiveInfinity;
                if (ageSeconds >= FlushStaleAgeSeconds)
                {
                    workersStale.Add(worker.Signal);
                    degradedReasons.Add($"{worker.Signal} flush stale ({FormatAge(ageSeconds)} since last flush, still receiving traffic)");
                }
            }
        }

        foreach (var stream in pipeline.Streams)
        {
            if (!stream.Available)
            {
                continue;
            }

            var pct = UtilizationPercent(stream);

            if (workersDown.Contains(stream.Signal))
            {
                if (stream.Length > 0)
                {
                    downReasons.Add(pct is { } p ? $"{FormatCount(stream.Length)} buffered ({p}%)" : $"{FormatCount(stream.Length)} buffered");
                }

                continue;
            }

            if (pct is { } dp && dp >= DownUtilizationPercent)
            {
                downReasons.Add($"{stream.Signal} buffer at {dp}% capacity, oldest entries at risk of being dropped");
                continue;
            }

            if (IsBacklogStuck(stream))
            {
                downReasons.Add($"{stream.Signal} pipeline backlog stuck ({FormatCount(stream.PendingCount)} pending)");
                continue;
            }

            if (workersStale.Contains(stream.Signal))
            {
                continue;
            }

            if (pct is { } wp && wp >= WarnUtilizationPercent)
            {
                degradedReasons.Add($"{stream.Signal} buffer at {wp}% capacity");
            }
            else if (stream.PendingCount > 0 || (stream.Lag ?? 0) > 0)
            {
                degradedReasons.Add($"{stream.Signal} backlog building ({FormatCount(stream.Length)} buffered)");
            }
        }

        if (totals.RejectedInWindow > 0)
        {
            degradedReasons.Add($"{Math.Round(rejectionRate * 100)}% rejected ({FormatCount(totals.RejectedInWindow)})");
        }

        if (downReasons.Count > 0)
        {
            return new Verdict("down", "Down", string.Join(" · ", downReasons.Concat(degradedReasons).Take(2)));
        }

        if (degradedReasons.Count > 0)
        {
            return new Verdict("degraded", "Degraded", string.Join(" · ", degradedReasons.Take(2)));
        }

        return new Verdict("healthy", "Healthy", "All receivers operational · 0% rejected · no pipeline backlog");
    }

    private static bool HasRecentArrivals(IReadOnlyList<IngestionBucketPointWire> buckets, string signal)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-RecentActivityLookbackMinutes);
        return buckets.Any(b => b.Signal == signal && b.Records > 0 && b.BucketStart >= cutoff);
    }

    private static bool IsBacklogStuck(PipelineStreamHealthWire stream) =>
        stream.PendingCount > 0 && (stream.OldestPendingAgeSeconds ?? 0) >= StuckBacklogAgeSeconds;

    private static int? UtilizationPercent(PipelineStreamHealthWire stream) =>
        stream.Capacity > 0 ? (int)Math.Round(100.0 * stream.Length / stream.Capacity) : null;

    private static void PrintVerdict(Verdict verdict)
    {
        var (dot, color) = verdict.Level switch
        {
            "healthy" => ("●", "green"),
            "degraded" => ("●", "yellow"),
            _ => ("●", "red"),
        };
        AnsiConsole.MarkupLine($"[{color}]{dot} {Markup.Escape(verdict.Label)}[/]");
        AnsiConsole.MarkupLine($"[grey]{Markup.Escape(verdict.Detail)}[/]");
    }

    private static void PrintRates(IngestionStatsResponseWire stats)
    {
        var t = stats.Totals;
        AnsiConsole.MarkupLine(
            $"Ingress [bold]{FormatCount(t.ArrivalsPerMinute)}[/] req/min · " +
            $"Events [bold]{FormatCount(t.IngestedRecordsPerMinute)}[/]/min · " +
            $"Data [bold]{FormatBytes(t.IngestedBytesPerMinute)}[/]/min · " +
            $"Window [bold]{FormatCount(t.RequestsInWindow)}[/] req · " +
            $"Rejected [bold {(t.RejectedInWindow > 0 ? "red" : "")}]{FormatCount(t.RejectedInWindow)}[/]");
    }

    // ---- Receivers - hand-port of computeReceiverStatus. ----

    private static void PrintReceivers(IngestionStatsResponseWire stats)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("Receivers");
        table.AddColumn("Receiver");
        table.AddColumn("Status");
        table.AddColumn(new TableColumn("Requests").RightAligned());

        foreach (var (protocol, label) in new[] { ("Grpc", "gRPC :4317"), ("Http", "HTTP :4318"), ("Scrape", "Prometheus scrape") })
        {
            var matching = stats.Buckets.Where(b => b.Protocol == protocol).ToList();
            var requests = matching.Sum(b => b.Requests);
            var rejected = matching.Sum(b => b.Rejected);

            var (statusText, statusColor) = requests == 0
                ? ("Idle", "grey")
                : rejected == 0
                    ? ("Healthy", "green")
                    : rejected >= requests
                        ? ("Down", "red")
                        : ("Degraded", "yellow");

            table.AddRow(label, $"[{statusColor}]{statusText}[/]", $"{FormatCount(requests)} req");
        }

        AnsiConsole.Write(table);
    }

    // ---- Pipeline buffers + flush workers, combined into one table per signal (the
    // dashboard keeps these as two separate tables; a terminal summary reads better as one
    // row per signal than two tables to cross-reference by eye). Hand-port of
    // computeFlushStatus. ----

    private static void PrintPipeline(IngestionStatsResponseWire stats, PipelineStatsResponseWire pipeline)
    {
        var table = new Table().Border(TableBorder.Rounded).Title("Pipeline");
        table.AddColumn("Signal");
        table.AddColumn(new TableColumn("Buffered").RightAligned());
        table.AddColumn(new TableColumn("Pending").RightAligned());
        table.AddColumn("Last flush");
        table.AddColumn("Status");
        table.AddColumn("Last error");

        var streamBySignal = pipeline.Streams.ToDictionary(s => s.Signal);
        var workerBySignal = pipeline.FlushWorkers.ToDictionary(w => w.Signal);

        foreach (var signal in new[] { "Logs", "Traces", "Metrics" })
        {
            streamBySignal.TryGetValue(signal, out var stream);
            workerBySignal.TryGetValue(signal, out var worker);

            var pct = stream is not null ? UtilizationPercent(stream) : null;
            var bufferedText = stream is null
                ? "—"
                : pct is { } p ? $"{FormatCount(stream.Length)} ({p}%)" : FormatCount(stream.Length);
            var bufferedColor = pct switch
            {
                { } dp when dp >= DownUtilizationPercent => "red",
                { } wp when wp >= WarnUtilizationPercent => "yellow",
                _ => "",
            };

            var pendingText = stream is null ? "—" : FormatCount(stream.PendingCount);
            var pendingColor = stream is not null && IsBacklogStuck(stream) ? "yellow" : "";

            var lastFlushText = worker?.LastFlushAt is { } at ? FormatAge((DateTimeOffset.UtcNow - at).TotalSeconds) : "never";

            var status = ComputeFlushStatus(worker, stream, stats);

            table.AddRow(
                signal,
                Colorize(bufferedText, bufferedColor),
                Colorize(pendingText, pendingColor),
                $"[grey]{Markup.Escape(lastFlushText)}[/]",
                $"[{status.Color}]{Markup.Escape(status.Label)}[/]",
                worker?.LastError is { } err ? $"[grey]{Markup.Escape(err)}[/]" : "[grey]—[/]");
        }

        AnsiConsole.Write(table);
    }

    private sealed record FlushStatus(string Label, string Color);

    private static FlushStatus ComputeFlushStatus(PipelineFlushHealthWire? worker, PipelineStreamHealthWire? stream, IngestionStatsResponseWire stats)
    {
        if (worker is null)
        {
            return new FlushStatus("Idle", "grey");
        }

        if (worker.ConsecutiveErrors >= DownConsecutiveErrors)
        {
            return new FlushStatus($"Down ({worker.ConsecutiveErrors})", "red");
        }

        if (worker.ConsecutiveErrors > 0)
        {
            return new FlushStatus($"Retrying ({worker.ConsecutiveErrors})", "yellow");
        }

        if (stream is not null && IsBacklogStuck(stream))
        {
            return new FlushStatus("Stuck", "yellow");
        }

        if (HasRecentArrivals(stats.Buckets, worker.Signal))
        {
            var ageSeconds = worker.LastFlushAt is { } at ? (DateTimeOffset.UtcNow - at).TotalSeconds : double.PositiveInfinity;
            if (ageSeconds >= FlushStaleAgeSeconds)
            {
                return new FlushStatus("Stale", "yellow");
            }
        }

        if (worker.LastError is not null)
        {
            return new FlushStatus("Recovered", "green");
        }

        return worker.LastFlushAt is null ? new FlushStatus("Idle", "grey") : new FlushStatus("Healthy", "green");
    }

    private static string Colorize(string text, string color) =>
        string.IsNullOrEmpty(color) ? Markup.Escape(text) : $"[{color}]{Markup.Escape(text)}[/]";

    // ---- Formatting - same compact-number/1024-byte conventions ingestion/format.ts uses,
    // reimplemented here rather than shared (no code-sharing boundary exists between the
    // dashboard's TS and this CLI's C#). ----

    private static string FormatCount(long n)
    {
        var abs = Math.Abs(n);
        if (abs < 1000)
        {
            return n.ToString(CultureInfo.InvariantCulture);
        }

        string[] suffixes = ["", "K", "M", "B", "T"];
        double v = n;
        var idx = 0;
        while (Math.Abs(v) >= 1000 && idx < suffixes.Length - 1)
        {
            v /= 1000;
            idx++;
        }

        return $"{v.ToString("0.#", CultureInfo.InvariantCulture)}{suffixes[idx]}";
    }

    private static string FormatBytes(long n)
    {
        if (n <= 0)
        {
            return "0 B";
        }

        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var exponent = Math.Min(units.Length - 1, (int)(Math.Log(n) / Math.Log(1024)));
        var value = n / Math.Pow(1024, exponent);
        var text = exponent == 0 ? value.ToString(CultureInfo.InvariantCulture) : value.ToString(value < 10 ? "0.#" : "0", CultureInfo.InvariantCulture);
        return $"{text} {units[exponent]}";
    }

    private static string FormatAge(double seconds)
    {
        if (double.IsPositiveInfinity(seconds))
        {
            return "never";
        }

        if (seconds < 1)
        {
            return "just now";
        }

        if (seconds < 60)
        {
            return $"{(int)seconds}s ago";
        }

        if (seconds < 3600)
        {
            return $"{(int)(seconds / 60)}m ago";
        }

        return $"{(int)(seconds / 3600)}h ago";
    }
}

// ---- Wire DTOs - hand-mirror of Flare.Api's Model/IngestionModels.cs + Model/
// PipelineModels.cs (see IngestionJsonContext/PipelineJsonContext's camelCase-properties/
// PascalCase-string-enum-values convention - Signal/Protocol serialize as "Logs"/"Grpc"
// etc. verbatim, same reason MetricsCommand's own wire DTOs keep MetricNameInfoWire.Type as
// a plain string instead of round-tripping through a C# enum). ----

internal sealed class IngestionBucketPointWire
{
    public DateTimeOffset BucketStart { get; init; }

    public required string Signal { get; init; }

    public required string Protocol { get; init; }

    public long Requests { get; init; }

    public long Records { get; init; }

    public long Bytes { get; init; }

    public long Rejected { get; init; }
}

internal sealed class IngestionStatsTotalsWire
{
    public long ArrivalsPerMinute { get; init; }

    public long IngestedRecordsPerMinute { get; init; }

    public long IngestedBytesPerMinute { get; init; }

    public long RequestsInWindow { get; init; }

    public long RejectedInWindow { get; init; }
}

internal sealed class IngestionStatsResponseWire
{
    public DateTimeOffset GeneratedAt { get; init; }

    public int Minutes { get; init; }

    public List<IngestionBucketPointWire> Buckets { get; init; } = [];

    public required IngestionStatsTotalsWire Totals { get; init; }
}

internal sealed class PipelineStreamHealthWire
{
    public required string Signal { get; init; }

    public bool Available { get; init; }

    public long Length { get; init; }

    public long Capacity { get; init; }

    public long? Lag { get; init; }

    public long PendingCount { get; init; }

    public int Consumers { get; init; }

    public double? OldestPendingAgeSeconds { get; init; }
}

internal sealed class PipelineFlushHealthWire
{
    public required string Signal { get; init; }

    public DateTimeOffset? LastFlushAt { get; init; }

    public long? LastBatchSize { get; init; }

    public DateTimeOffset? LastErrorAt { get; init; }

    public string? LastError { get; init; }

    public long ConsecutiveErrors { get; init; }
}

internal sealed class PipelineStatsResponseWire
{
    public DateTimeOffset GeneratedAt { get; init; }

    public List<PipelineStreamHealthWire> Streams { get; init; } = [];

    public List<PipelineFlushHealthWire> FlushWorkers { get; init; } = [];
}

internal static class IngestionWireJsonOptions
{
    public static readonly JsonSerializerOptions Instance = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };
}
