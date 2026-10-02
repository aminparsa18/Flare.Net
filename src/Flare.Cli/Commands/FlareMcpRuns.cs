using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Flare.Cli.Commands;

/// <summary>
/// "Run" detection for the MCP tools: a run of a service is one process start, identified by
/// its <c>service.instance.id</c> resource attribute (a fresh value per start under Aspire and
/// the OTel .NET SDK). Replicas of one service started together belong to the same run, so
/// instance first-seen times within <see cref="ClusterWindow"/> of each other collapse into one.
/// </summary>
internal static class RunBoundaries
{
    public static readonly TimeSpan ClusterWindow = TimeSpan.FromSeconds(60);

    /// <summary>Range filters exclude their exact lower bound, and a run's start IS its first span's timestamp - so scoped queries start this much earlier.</summary>
    public static readonly TimeSpan QueryMargin = TimeSpan.FromSeconds(1);

    /// <summary>Run start times, newest first, from each instance's first-seen time.</summary>
    public static List<DateTimeOffset> Cluster(IEnumerable<DateTimeOffset> instanceFirstSeen)
    {
        var starts = new List<DateTimeOffset>();
        DateTimeOffset? clusterStart = null;
        foreach (var t in instanceFirstSeen.OrderByDescending(x => x))
        {
            if (clusterStart is { } current && current - t <= ClusterWindow)
            {
                clusterStart = t; // same run - pull its start back to the earliest member
                starts[^1] = t;
            }
            else
            {
                clusterStart = t;
                starts.Add(t);
            }
        }

        return starts;
    }
}

internal sealed class RunLocator(FlareApiClient api)
{
    private const int MaxInstances = 12;

    /// <summary>Run start times for one service over the last 7 days, newest first; empty when it reports no service.instance.id.</summary>
    public async Task<List<DateTimeOffset>> GetRunStartsAsync(string service, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var from = now.AddDays(-7);

        using var valuesResponse = await api.Http.PostAsJsonAsync(
            "/api/spans/attribute-values",
            new { filter = new { from, to = now, services = new[] { service } }, bag = "Resource", key = "service.instance.id", limit = 25 },
            WireJsonOptions.Instance,
            cancellationToken);
        valuesResponse.EnsureSuccessStatusCode();

        var values = await valuesResponse.Content.ReadFromJsonAsync<AttributeValuesWire>(WireJsonOptions.Instance, cancellationToken);
        var firstSeen = new List<DateTimeOffset>();
        foreach (var v in (values?.Values ?? []).Take(MaxInstances))
        {
            var request = new SpanSearchRequestWire
            {
                PageSize = 1,
                SortBy = "StartTime",
                SortAscending = true,
                Filter = new SpanFilterWire
                {
                    From = from,
                    To = now,
                    Services = [service],
                    Attributes = [new SpanAttributeFilterWire { Bag = "Resource", Key = "service.instance.id", Value = v.Value }],
                },
            };
            using var response = await api.Http.PostAsJsonAsync("/api/spans/search", request, WireJsonOptions.Instance, cancellationToken);
            response.EnsureSuccessStatusCode();

            var spans = (await response.Content.ReadFromJsonAsync<SpanSearchResponseWire>(WireJsonOptions.Instance, cancellationToken))?.Spans;
            if (spans is { Count: > 0 })
            {
                firstSeen.Add(spans[0].StartTime);
            }
        }

        return RunBoundaries.Cluster(firstSeen);
    }
}

internal sealed class AttributeValuesWire
{
    public List<AttributeValueWire> Values { get; init; } = [];
}

internal sealed class AttributeValueWire
{
    public string Value { get; init; } = "";

    public long Count { get; init; }
}

/// <summary>
/// Structural diff of two traces for the MCP before/after tool. Spans are matched by
/// "service name" (not span id, which differs every run); per key it compares occurrence
/// count, mean duration and error count.
/// </summary>
internal static class TraceDiff
{
    private const double RelativeThreshold = 0.20;
    private const double AbsoluteThresholdMs = 5;
    private const int MaxLines = 40;

    public static string Render(IReadOnlyList<SpanDtoWire> baseline, IReadOnlyList<SpanDtoWire> candidate)
    {
        var a = Summarize(baseline);
        var b = Summarize(candidate);

        var added = new List<string>();
        var removed = new List<string>();
        var slower = new List<(double Delta, string Line)>();
        var errors = new List<string>();

        foreach (var (key, cur) in b)
        {
            if (!a.TryGetValue(key, out var prev))
            {
                added.Add($"+ {key} x{cur.Count} ({FormatMs(cur.MeanMs)}{(cur.Errors > 0 ? $", {cur.Errors} errors" : "")})");
                continue;
            }

            var delta = cur.MeanMs - prev.MeanMs;
            if (Math.Abs(delta) >= AbsoluteThresholdMs && Math.Abs(delta) >= RelativeThreshold * Math.Max(prev.MeanMs, 0.001))
            {
                slower.Add((delta, $"~ {key}: {FormatMs(prev.MeanMs)} -> {FormatMs(cur.MeanMs)} ({(delta > 0 ? "+" : "-")}{FormatMs(Math.Abs(delta))}){(cur.Count != prev.Count ? $", count {prev.Count} -> {cur.Count}" : "")}"));
            }
            else if (cur.Count != prev.Count)
            {
                slower.Add((0, $"~ {key}: count {prev.Count} -> {cur.Count}"));
            }

            if (cur.Errors != prev.Errors)
            {
                errors.Add($"! {key}: errors {prev.Errors} -> {cur.Errors}");
            }
        }

        foreach (var (key, prev) in a)
        {
            if (!b.ContainsKey(key))
            {
                removed.Add($"- {key} x{prev.Count} ({FormatMs(prev.MeanMs)})");
            }
        }

        var sb = new StringBuilder();
        sb.AppendLine($"baseline: {baseline.Count} spans, {Total(baseline)} total, {baseline.Count(s => s.StatusCode == "STATUS_CODE_ERROR")} errors");
        sb.AppendLine($"candidate: {candidate.Count} spans, {Total(candidate)} total, {candidate.Count(s => s.StatusCode == "STATUS_CODE_ERROR")} errors");

        var lines = added.Concat(removed).Concat(errors).Concat(slower.OrderByDescending(x => Math.Abs(x.Delta)).Select(x => x.Line)).ToList();
        if (lines.Count == 0)
        {
            sb.AppendLine("No structural, error or significant duration differences.");
            return sb.ToString();
        }

        foreach (var line in lines.Take(MaxLines))
        {
            sb.AppendLine(line);
        }

        if (lines.Count > MaxLines)
        {
            sb.AppendLine($"(+{lines.Count - MaxLines} more differences not shown)");
        }

        return sb.ToString();
    }

    private static Dictionary<string, (int Count, double MeanMs, int Errors)> Summarize(IReadOnlyList<SpanDtoWire> spans) =>
        spans.GroupBy(s => $"{s.ServiceName} {s.Name}").ToDictionary(
            g => g.Key,
            g => (g.Count(), g.Average(s => s.DurationNano / 1_000_000.0), g.Count(s => s.StatusCode == "STATUS_CODE_ERROR")));

    private static string Total(IReadOnlyList<SpanDtoWire> spans) =>
        spans.Count == 0 ? "0ms" : FormatMs((spans.Max(s => s.EndTime) - spans.Min(s => s.StartTime)).TotalMilliseconds);

    private static string FormatMs(double ms) =>
        ms >= 1000 ? $"{ms / 1000:0.##}s" : $"{ms:0.##}ms";
}
