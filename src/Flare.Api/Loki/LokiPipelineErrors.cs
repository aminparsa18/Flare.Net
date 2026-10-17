using System.Text.RegularExpressions;

namespace Flare.Api.Loki;

/// <summary>
/// Loki's rule for lines that fail a pipeline stage (ADR-0238). A line whose value cannot be unwrapped carries
/// <c>__error__="SampleExtractionErr"</c>; a metric query that meets such a line fails with a "pipeline error"
/// unless its pipeline asks to keep the errored lines, which any label filter on <c>__error__</c> does when it
/// accepts a non-empty error value (<c>| __error__!=""</c>, <c>| __error__="SampleExtractionErr"</c>). A
/// filter that accepts only the empty value (<c>| __error__=""</c>) removes the errored lines before they become
/// samples, so the query succeeds. Pure.
/// </summary>
internal static class LokiPipelineErrors
{
    public const string Label = "__error__";

    public const string SampleExtraction = "SampleExtractionErr";

    /// <summary>Every value a pipeline sets <c>__error__</c> to, as Loki lists them.</summary>
    private static readonly string[] ErrorValues = ["JSONParserErr", "LogfmtParserErr", SampleExtraction, "LabelFilterErr", "TemplateFormatErr"];

    /// <summary>True when a label filter in <paramref name="stages"/> asks to keep the lines that carry <c>__error__</c>.</summary>
    public static bool KeepsErroredLines(IEnumerable<LokiStage> stages) =>
        stages.OfType<LokiLabelFilterStage>().Any(stage => Keeps(stage.Predicate));

    private static bool Keeps(LokiPredicate predicate) => predicate switch
    {
        LokiAnd both => Keeps(both.Left) || Keeps(both.Right),
        LokiOr either => Keeps(either.Left) || Keeps(either.Right),
        LokiComparison { Label: Label, Number: null } comparison => KeepsErrors(comparison),
        _ => false,
    };

    /// <summary>A comparison that rejects the empty value asks for the errored lines; so does one that accepts any error value.</summary>
    private static bool KeepsErrors(LokiComparison comparison) =>
        !Passes(comparison, string.Empty) || ErrorValues.Any(value => Passes(comparison, value));

    private static bool Passes(LokiComparison comparison, string value)
    {
        switch (comparison.Op)
        {
            case LokiCompareOp.Equal:
                return value == comparison.Text;
            case LokiCompareOp.NotEqual:
                return value != comparison.Text;
            case LokiCompareOp.Regex or LokiCompareOp.NotRegex:
                try
                {
                    var matches = Regex.IsMatch(value, $"^(?:{comparison.Text})$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                    return matches == (comparison.Op == LokiCompareOp.Regex);
                }
                catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
                {
                    return false;
                }

            default:
                return false;
        }
    }

    /// <summary>Loki's error for a metric query that met an errored line it was not asked to keep.</summary>
    public static LokiException PipelineError(string errorType, IReadOnlyDictionary<string, string> series)
    {
        var labels = "{" + string.Join(", ", series.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}=\"{kv.Value}\"")) + "}";
        return new LokiException(
            $"pipeline error: '{errorType}' for resulting series: '{labels}'.\n" +
            $"Use a label filter to intentionally skip this error. (e.g | __error__!=\"{errorType}\").\n" +
            "To skip all potential errors you can match empty errors.(e.g __error__=\"\")\n" +
            "The label filter can also be specified after unwrap. (e.g | unwrap latency | __error__=\"\" )\n");
    }
}
