using System.Text;

namespace Flare.Api.Errors;

/// <summary>
/// Collapses the variable parts of an <c>exception.message</c> (ids, counts, GUIDs, quoted values)
/// so occurrences that differ only by those parts share one error group (ADR-0258). The rules are
/// applied in ClickHouse (<c>replaceRegexpAll</c>, RE2) rather than in C#, so the group key a query
/// computes and the one stored in <c>error_issues</c> can never disagree: there is one
/// implementation, and <see cref="Sql"/> is its only entry point.
/// </summary>
/// <remarks>
/// Normalization is idempotent (the placeholders contain no digits, quotes or hex), which is what
/// lets a caller pass either a raw message or an already-normalized group message and get the same
/// match (<see cref="MatchSql"/>). Rule order matters: GUIDs and <c>0x</c> hex before bare numbers.
/// Migration <c>0087_error_message_normalization.sql</c> repeats these patterns to re-key existing
/// triage state; change a rule here and a new migration must re-key again.
/// </remarks>
public static class ErrorMessageNormalizer
{
    /// <summary>(RE2 pattern, replacement) pairs, applied top to bottom. Replacement backreferences use RE2's <c>\1</c> form.</summary>
    public static readonly IReadOnlyList<(string Pattern, string Replacement)> Rules =
    [
        (@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", "<guid>"),
        (@"\b0[xX][0-9a-fA-F]+\b", "<hex>"),
        // ISO-8601 timestamps as one token: bare digit runs glued to a 'T' ("20T10") have no word boundary for the number rule.
        (@"\b[0-9]{4}-[0-9]{2}-[0-9]{2}[T ][0-9]{2}:[0-9]{2}:[0-9]{2}(\.[0-9]+)?(Z|[+-][0-9]{2}:?[0-9]{2})?", "<ts>"),
        // Quoted values: only a quote that opens a token ('x' after start/space/bracket/=/:/,), so the
        // apostrophe in "can't" / "it's" never pairs up with a later quote.
        (@"(^|[\s(\[{=:,])'[^'\n]*'", @"\1'<s>'"),
        (@"(^|[\s(\[{=:,])""[^""\n]*""", @"\1""<s>"""),
        (@"\b[0-9]+\b", "<n>"),
    ];

    /// <summary>
    /// The SQL expression that normalizes <paramref name="messageExpression"/> (any String-typed
    /// expression, e.g. <c>EventAttributes['exception.message']</c>).
    /// </summary>
    public static string Sql(string messageExpression)
    {
        var expression = messageExpression;
        foreach (var (pattern, replacement) in Rules)
        {
            expression = $"replaceRegexpAll({expression}, {Literal(pattern)}, {Literal(replacement)})";
        }

        return expression;
    }

    /// <summary>
    /// A predicate matching rows whose message belongs to the group named by <paramref name="parameterName"/>
    /// (a bound <c>String</c> parameter holding a group message <em>or</em> a raw one: both normalize to the same key).
    /// </summary>
    public static string MatchSql(string messageExpression, string parameterName) =>
        $"{Sql(messageExpression)} = {Sql("{" + parameterName + ":String}")}";

    /// <summary>A ClickHouse string literal for <paramref name="value"/>: backslash and quote escaped, control characters as <c>\xHH</c>.</summary>
    internal static string Literal(string value)
    {
        var builder = new StringBuilder("'", value.Length + 8);
        foreach (var c in value)
        {
            if (c is '\\' or '\'')
            {
                builder.Append('\\').Append(c);
            }
            else if (c < ' ')
            {
                builder.Append("\\x").Append(((int)c).ToString("x2"));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.Append('\'').ToString();
    }
}
