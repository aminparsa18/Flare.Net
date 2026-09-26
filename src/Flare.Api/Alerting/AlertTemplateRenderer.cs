using System.Text;
using System.Text.RegularExpressions;

namespace Flare.Api.Alerting;

/// <summary>
/// The placeholder substitution behind <see cref="Model.AlertRule.NotificationTitleTemplate"/>/
/// <see cref="Model.AlertRule.NotificationBodyTemplate"/> - deliberately a single regex
/// replace over <c>{{name}}</c> tokens, not a template engine: no conditionals, loops,
/// filters, expressions or escaping, so a template can never execute anything or read
/// anything beyond the fixed value set <see cref="AlertMessageFormatter.BuildTemplateValues"/>
/// hands it. See <c>docs-internal/adr/0052-alert-notification-templates.md</c>.
/// </summary>
/// <remarks>
/// A placeholder is either one of <see cref="Names"/> or <c>labels.&lt;key&gt;</c>, where the
/// key may itself be dotted (<c>{{labels.service.name}}</c>) - that is why a name is matched
/// as <c>[A-Za-z0-9_.\-]+</c> rather than split on dots. Anything that doesn't look like a
/// placeholder at all (a lone <c>{{</c>, <c>{{ }}</c>, JSON braces) is left as literal text.
/// </remarks>
public static partial class AlertTemplateRenderer
{
    public const string LabelsPrefix = "labels.";

    /// <summary>Every non-label placeholder a template may use - also what the rule form lists.</summary>
    public static readonly IReadOnlyList<string> Names =
    [
        "rule_name", "rule_id", "description", "status", "condition_kind",
        "value", "threshold", "comparator", "window", "window_seconds",
        "metric", "exception_type", "baseline_mean", "z_score",
        "fired_at", "rule_url", "logs_url", "data_url", "message",
    ];

    private static readonly HashSet<string> NameSet = new(Names, StringComparer.Ordinal);

    [GeneratedRegex(@"\{\{\s*([A-Za-z0-9_.\-]+)\s*\}\}")]
    private static partial Regex PlaceholderRegex();

    /// <summary>
    /// Substitutes every known placeholder in <paramref name="template"/>. A label the rule
    /// isn't scoped by renders empty (there's nothing to report); an unknown name - only
    /// possible for a rule saved before <see cref="Validate"/> knew about it being removed -
    /// is left verbatim rather than silently dropped.
    /// </summary>
    public static string Render(string template, IReadOnlyDictionary<string, string> values, IReadOnlyDictionary<string, string> labels) =>
        PlaceholderRegex().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            if (name.StartsWith(LabelsPrefix, StringComparison.Ordinal))
            {
                return labels.TryGetValue(name[LabelsPrefix.Length..], out var label) ? label : "";
            }

            return values.TryGetValue(name, out var value) ? value : match.Value;
        });

    /// <summary>
    /// Null when <paramref name="template"/> only uses known placeholders and fits in
    /// <paramref name="maxLength"/>; otherwise a message naming the first problem.
    /// <paramref name="field"/> is the request member's JSON name, for the message.
    /// </summary>
    public static string? Validate(string? template, string field, int maxLength)
    {
        if (string.IsNullOrEmpty(template))
        {
            return null;
        }

        if (template.Length > maxLength)
        {
            return $"{field} must be at most {maxLength} characters.";
        }

        List<string>? unknown = null;
        foreach (Match match in PlaceholderRegex().Matches(template))
        {
            var name = match.Groups[1].Value;
            var known = NameSet.Contains(name)
                || (name.StartsWith(LabelsPrefix, StringComparison.Ordinal) && name.Length > LabelsPrefix.Length);
            if (!known && !(unknown ??= []).Contains(name))
            {
                unknown.Add(name);
            }
        }

        if (unknown is null)
        {
            return null;
        }

        var message = new StringBuilder($"{field} uses unknown placeholder(s): ");
        message.AppendJoin(", ", unknown.Select(n => "{{" + n + "}}"));
        message.Append(". Supported: ");
        message.AppendJoin(", ", Names.Select(n => "{{" + n + "}}"));
        message.Append(", {{labels.<key>}}.");
        return message.ToString();
    }
}
