using System.Text;
using Flare.Api.Model;

namespace Flare.Api.Ai;

/// <summary>
/// Builds the redacted, size-bounded prompt for "Explain this exception" (ADR-0103). Pure: the
/// exact string returned here is what is sent to the model and what is recorded in the log.
/// </summary>
public static class ExplainExceptionPromptBuilder
{
    public const string SystemPrompt =
        "You are a senior software engineer helping to debug a production exception. " +
        "Explain in plain language what the exception means, the most likely root cause given the stack trace " +
        "and source, and concrete next steps to fix or confirm it. Be concise (under 250 words). " +
        "If the information is insufficient, say what is missing. Values marked [REDACTED] were removed for privacy.";

    /// <param name="sourceStartLine">1-based number of <paramref name="sourceLines"/>[0]; ignored when no source.</param>
    /// <param name="maxChars">Cap on the whole user prompt after redaction; stack trace is cut first, then source.</param>
    public static string Build(
        ExplainExceptionRequest request,
        string? sourcePath,
        int sourceStartLine,
        IReadOnlyList<string>? sourceLines,
        int maxChars)
    {
        var header = new StringBuilder()
            .Append("Service: ").AppendLine(AiRedactor.Redact(request.ServiceName))
            .Append("Exception type: ").AppendLine(AiRedactor.Redact(request.ExceptionType))
            .Append("Message: ").AppendLine(AiRedactor.Redact(request.ExceptionMessage ?? ""))
            .ToString();

        var source = "";
        if (sourceLines is { Count: > 0 } && !string.IsNullOrEmpty(sourcePath))
        {
            var sb = new StringBuilder().AppendLine().Append("Source around the throw site (").Append(AiRedactor.Redact(sourcePath)).AppendLine("):");
            for (var i = 0; i < sourceLines.Count; i++)
            {
                sb.Append(sourceStartLine + i).Append(": ").AppendLine(AiRedactor.Redact(sourceLines[i]));
            }

            source = sb.ToString();
        }

        var trace = AiRedactor.Redact(request.Stacktrace ?? "");
        const string StackHeading = "\nStack trace:\n";

        var remaining = Math.Max(0, maxChars - header.Length);
        // Stack trace gets priority; source fills what's left of the budget.
        var traceBudget = Math.Max(0, remaining - StackHeading.Length);
        var traceText = Truncate(trace, traceBudget);
        var sourceBudget = Math.Max(0, remaining - StackHeading.Length - traceText.Length);
        var sourceText = Truncate(source, sourceBudget);

        var prompt = new StringBuilder(header);
        if (traceText.Length > 0)
        {
            prompt.Append(StackHeading).Append(traceText);
        }

        prompt.Append(sourceText.Length > 0 ? "\n" + sourceText : "");
        return prompt.ToString();
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max] + (max > 0 ? "\n[truncated]" : "");
}
