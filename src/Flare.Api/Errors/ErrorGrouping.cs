using Flare.Api.Model;

namespace Flare.Api.Errors;

/// <summary>
/// How exception events are grouped on <c>/errors</c> (ADR-0258, ADR-0259): the built-in message normalization (when on),
/// then the custom regular-expression replacements in order, then the merges. <see cref="Sql"/> turns that into one
/// ClickHouse expression, which every query that keys on a group embeds, so a group means the same thing in the list,
/// the drill-down, alerts, issue state and release tracking.
/// </summary>
public sealed class ErrorGrouping
{
    /// <summary>The <c>exception.type</c> / <c>exception.message</c> of an <c>ARRAY JOIN</c>ed span event, as every group query spells them.</summary>
    public const string EventType = "EventAttributes['exception.type']";
    public const string EventMessage = "EventAttributes['exception.message']";

    /// <summary>Built-in normalization only: what an instance with no custom rules does.</summary>
    public static ErrorGrouping Default { get; } = new(true, [], []);

    private readonly bool _normalize;
    private readonly IReadOnlyList<(string Pattern, string Replacement)> _replacements;
    private readonly IReadOnlyList<(string Type, string From, string To)> _merges;

    private ErrorGrouping(bool normalize, IReadOnlyList<(string, string)> replacements, IReadOnlyList<(string, string, string)> merges)
    {
        _normalize = normalize;
        _replacements = replacements;
        _merges = merges;
    }

    public static ErrorGrouping From(ErrorGroupingDocument document)
    {
        var replacements = document.Rules
            .Where(r => r.Kind == ErrorGroupingKind.Replace)
            .Select(r => (r.Pattern, r.Replacement))
            .ToList();
        var merges = ResolveMergeChains(document.Rules.Where(r => r.Kind == ErrorGroupingKind.Merge).Select(r => (r.ExceptionType, r.Pattern, r.Replacement)).ToList());
        return new ErrorGrouping(document.NormalizeMessages, replacements, merges);
    }

    /// <summary>
    /// Merges are one lookup, not repeated passes, so A into B plus B into C must read A into C. A repeated source keeps its
    /// first rule, and a cycle (A into B, B into A) keeps the rule that closes it out of the result rather than looping.
    /// </summary>
    internal static IReadOnlyList<(string Type, string From, string To)> ResolveMergeChains(IReadOnlyList<(string Type, string From, string To)> merges)
    {
        var first = new Dictionary<(string, string), string>();
        foreach (var (type, from, to) in merges)
        {
            first.TryAdd((type, from), to);
        }

        var resolved = new List<(string, string, string)>();
        foreach (var ((type, from), to) in first)
        {
            var target = to;
            var seen = new HashSet<string> { from };
            while (first.TryGetValue((type, target), out var next) && seen.Add(target))
            {
                target = next;
            }

            if (seen.Contains(target))
            {
                continue; // cycle: no consistent target
            }

            resolved.Add((type, from, target));
        }

        return resolved;
    }

    /// <summary>
    /// The group message of a row whose type and message are the given String expressions. Idempotent for the built-in
    /// rules; custom replacements are required to be too (see <c>ErrorGroupingQueryService.ValidateAsync</c>), which is
    /// what lets <see cref="MatchSql"/> accept a raw message or a group's message alike.
    /// </summary>
    public string Sql(string typeExpression, string messageExpression)
    {
        var expression = _normalize ? ErrorMessageNormalizer.Sql(messageExpression) : messageExpression;
        foreach (var (pattern, replacement) in _replacements)
        {
            expression = $"replaceRegexpAll({expression}, {ErrorMessageNormalizer.Literal(pattern)}, {ErrorMessageNormalizer.Literal(replacement)})";
        }

        if (_merges.Count > 0)
        {
            var keys = string.Join(", ", _merges.Select(m => ErrorMessageNormalizer.Literal(m.Type + ErrorIssueFingerprint.KeySeparator + m.From)));
            var targets = string.Join(", ", _merges.Select(m => ErrorMessageNormalizer.Literal(m.To)));
            expression = $"transform(concat({typeExpression}, char(31), {expression}), [{keys}], [{targets}], {expression})";
        }

        return expression;
    }

    /// <summary><see cref="Sql(string, string)"/> over a span event's attributes.</summary>
    public string EventMessageSql() => Sql(EventType, EventMessage);

    /// <summary>The group key (<see cref="ErrorIssueFingerprint.Key"/>) of a span event, computed in SQL.</summary>
    public string EventKeySql() => $"concat({EventType}, char(31), {EventMessageSql()})";

    /// <summary>
    /// A predicate matching span events in the group named by two bound <c>String</c> parameters: the group message
    /// of the row equals that of the parameters, so a raw message works as well as the group's own.
    /// </summary>
    public string MatchSql(string typeParameter, string messageParameter) =>
        $"{EventMessageSql()} = {Sql("{" + typeParameter + ":String}", "{" + messageParameter + ":String}")}";
}
