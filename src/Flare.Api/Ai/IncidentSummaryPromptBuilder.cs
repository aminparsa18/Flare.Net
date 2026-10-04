using System.Globalization;
using System.Text;
using Flare.Api.Model;

namespace Flare.Api.Ai;

/// <summary>
/// Builds the redacted, size-bounded prompt for an alert's AI incident summary (ADR-0104). Pure:
/// the exact string returned is what is sent to the model and what is stored with the summary.
/// Sections are added in priority order and the first one that would overflow
/// <c>maxChars</c> (and everything after it) is dropped, so the alert itself is always included.
/// </summary>
public static class IncidentSummaryPromptBuilder
{
    public const string SystemPrompt =
        "You are an on-call engineer writing a short incident summary for an alert that just fired. " +
        "Using only the evidence provided, state: (1) what happened and the first/primary error, " +
        "(2) which service or span is failing, (3) what changed compared with the previous window, " +
        "(4) the most likely cause and one concrete next step. Plain text, at most 120 words, no markdown. " +
        "If the evidence is thin or inconclusive, say so instead of guessing. Values marked [REDACTED] were removed for privacy.";

    private const int MaxLineChars = 300;

    public static string Build(IncidentEvidence evidence, int maxChars)
    {
        var sections = new List<string> { Header(evidence) };

        if (!evidence.NoData && evidence.Previous is { } previous)
        {
            sections.Add($"Previous window (same length, immediately before): {Format(previous, evidence.MetricUnit)}; current: {Format(evidence.Observed, evidence.MetricUnit)}.");
        }

        if (evidence.Exceptions.Count > 0)
        {
            sections.Add("Exceptions in the window (most frequent first):\n" + string.Join("\n", evidence.Exceptions.Select(e =>
                $"- {e.Count}x {Clip(e.Type)}: {Clip(e.Message)} (service {Clip(e.Service)})")));
        }

        if (evidence.LogPatterns.Count > 0)
        {
            sections.Add("Log patterns in the window (highest severity, then most frequent first):\n" + string.Join("\n", evidence.LogPatterns.Select(p =>
                $"- {p.Count}x [{SeverityName(p.SeverityNumber)}] {Clip(p.Service)}: {Clip(p.Template)}")));
        }

        if (evidence.ErrorSpans.Count > 0)
        {
            sections.Add("Failing spans of one representative trace:\n" + string.Join("\n", evidence.ErrorSpans.Select(s =>
                $"- {Clip(s.Service)} / {Clip(s.Name)} ({s.DurationMs.ToString("0.#", CultureInfo.InvariantCulture)} ms)" +
                (string.IsNullOrWhiteSpace(s.StatusMessage) ? "" : $": {Clip(s.StatusMessage)}"))));
        }

        var prompt = new StringBuilder();
        foreach (var section in sections)
        {
            var redacted = AiRedactor.Redact(section);
            if (prompt.Length > 0 && prompt.Length + redacted.Length + 2 > maxChars)
            {
                break;
            }

            if (prompt.Length > 0)
            {
                prompt.Append("\n\n");
            }

            prompt.Append(prompt.Length == 0 && redacted.Length > maxChars ? redacted[..Math.Max(0, maxChars)] : redacted);
        }

        return prompt.ToString();
    }

    private static string Header(IncidentEvidence e)
    {
        var rule = e.Rule;
        var sb = new StringBuilder();
        sb.Append("Alert: ").Append(Clip(rule.Name));
        if (!string.IsNullOrWhiteSpace(rule.Description))
        {
            sb.Append(" - ").Append(Clip(rule.Description));
        }

        sb.Append("\nSeverity: ").Append(rule.Severity);
        sb.Append("\nWindow: ").Append(e.From.UtcDateTime.ToString("u", CultureInfo.InvariantCulture)).Append(" to ").Append(e.To.UtcDateTime.ToString("u", CultureInfo.InvariantCulture));
        if (e.NoData)
        {
            sb.Append("\nWhat fired: no data at all matched the rule's condition over the window (absent-data alert).");
            return sb.ToString();
        }

        var cmp = rule.Threshold.Comparator;
        switch (rule.ConditionKind)
        {
            case AlertConditionKind.MetricThreshold:
                sb.Append("\nWhat fired: metric ").Append(Clip(rule.MetricCondition?.MetricName ?? "?")).Append(' ').Append(rule.MetricCondition?.Aggregation)
                    .Append(" = ").Append(Format(e.Observed, e.MetricUnit)).Append(", threshold ").Append(cmp).Append(' ')
                    .Append(Format(rule.MetricThresholdValue ?? double.NaN, e.MetricUnit));
                AppendServices(sb, rule.MetricCondition?.Filter.Services);
                break;
            case AlertConditionKind.Anomaly:
                sb.Append("\nWhat fired: anomaly - current ").Append(Format(e.Observed, e.MetricUnit));
                if (e.Anomaly is { BaselineMean: { } mean, ZScore: { } z })
                {
                    sb.Append(", seasonal baseline ").Append(Format(mean, e.MetricUnit)).Append(", z-score ").Append(z.ToString("0.#", CultureInfo.InvariantCulture));
                }

                break;
            case AlertConditionKind.ExceptionCount:
                sb.Append("\nWhat fired: ").Append(Format(e.Observed, null)).Append(" occurrences of exception ").Append(Clip(rule.ExceptionCondition?.ExceptionType ?? "?"))
                    .Append(", threshold ").Append(cmp).Append(' ').Append(rule.Threshold.Count);
                AppendServices(sb, rule.ExceptionCondition?.Filter.Services);
                break;
            default:
                sb.Append("\nWhat fired: ").Append(Format(e.Observed, null)).Append(" matching log events, threshold ").Append(cmp).Append(' ').Append(rule.Threshold.Count);
                AppendServices(sb, rule.Condition.Services);
                if (!string.IsNullOrWhiteSpace(rule.Condition.Search))
                {
                    sb.Append("\nMessage contains: ").Append(Clip(rule.Condition.Search));
                }

                break;
        }

        return sb.ToString();
    }

    private static void AppendServices(StringBuilder sb, IReadOnlyList<string>? services)
    {
        if (services is { Count: > 0 })
        {
            sb.Append("\nServices: ").Append(Clip(string.Join(", ", services.Take(10))));
        }
    }

    private static string Format(double value, string? unit) =>
        double.IsNaN(value) ? "n/a" : value.ToString("G6", CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(unit) ? "" : " " + unit);

    private static string Clip(string text)
    {
        var line = text.ReplaceLineEndings(" ").Trim();
        return line.Length <= MaxLineChars ? line : line[..MaxLineChars] + "...";
    }

    private static string SeverityName(byte number) => number switch
    {
        >= 21 => "FATAL",
        >= 17 => "ERROR",
        >= 13 => "WARN",
        >= 9 => "INFO",
        _ => "DEBUG",
    };
}
