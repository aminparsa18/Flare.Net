using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Flare.Api.Retention;

/// <summary>
/// Per-resource retention rules (docs-internal/adr/0145-per-resource-retention.md): builds the
/// <c>multiIf</c> that becomes the <c>_retention_days</c> column default, and parses it back out of
/// <c>system.columns.default_expression</c> so what ClickHouse really has can be compared with what was asked for.
/// </summary>
public static partial class RetentionRuleSql
{
    public const int MaxRules = 20;

    public const int MaxValueLength = 200;

    /// <summary>Attribute keys are interpolated into SQL, so they are restricted to what OTel semantic conventions and real exporters use rather than escaped.</summary>
    [GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.\-/:]{0,127}$", RegexOptions.CultureInvariant)]
    private static partial Regex AttributeKey();

    /// <summary>Null when the rules are usable, otherwise what's wrong.</summary>
    public static string? Validate(string signal, IReadOnlyList<RetentionRule>? rules)
    {
        if (rules is null)
        {
            return null;
        }

        if (rules.Count > MaxRules)
        {
            return $"{signal}: at most {MaxRules} rules.";
        }

        foreach (var rule in rules)
        {
            if (!AttributeKey().IsMatch(rule.Attribute ?? ""))
            {
                return $"{signal}: rule attribute '{rule.Attribute}' isn't a valid resource attribute key.";
            }

            if (string.IsNullOrEmpty(rule.Value) || rule.Value.Length > MaxValueLength || rule.Value.Any(char.IsControl))
            {
                return $"{signal}: rule value for '{rule.Attribute}' must be 1-{MaxValueLength} characters with no control characters.";
            }

            if (rule.Days is < 0 or > RetentionSql.MaxDays)
            {
                return $"{signal}: rule days must be between 0 (keep forever) and {RetentionSql.MaxDays}.";
            }
        }

        return null;
    }

    /// <summary>
    /// The DEFAULT expression for <c>_retention_days</c>: <c>multiIf(&lt;match&gt;, days, ..., default)</c>,
    /// or just the number when there are no rules. 0 days (forever) is written as
    /// <see cref="RetentionSql.ForeverDays"/>, since a TTL of 0 days would delete rows immediately.
    /// </summary>
    public static string BuildDefaultExpression(IReadOnlyList<RetentionRule> rules, int defaultDays)
    {
        var fallback = Effective(defaultDays).ToString(CultureInfo.InvariantCulture);
        if (rules.Count == 0)
        {
            return fallback;
        }

        var sb = new StringBuilder("multiIf(");
        foreach (var rule in rules)
        {
            sb.Append($"ResourceAttributes['{rule.Attribute}'] = '{EscapeLiteral(rule.Value)}', {Effective(rule.Days)}, ");
        }

        return sb.Append(fallback).Append(')').ToString();
    }

    /// <summary>
    /// Reads a default expression back. Only the shapes <see cref="BuildDefaultExpression"/> writes
    /// (as ClickHouse re-prints them) are understood; anything else returns null.
    /// </summary>
    public static (IReadOnlyList<RetentionRule> Rules, int DefaultDays)? ParseDefaultExpression(string expression)
    {
        expression = expression.Trim();
        if (int.TryParse(expression, NumberStyles.None, CultureInfo.InvariantCulture, out var plain))
        {
            return ([], FromEffective(plain));
        }

        var match = MultiIf().Match(expression);
        if (!match.Success)
        {
            return null;
        }

        var rules = new List<RetentionRule>();
        foreach (Match rule in RuleBranch().Matches(match.Groups["branches"].Value))
        {
            rules.Add(new RetentionRule
            {
                Attribute = rule.Groups["key"].Value,
                Value = UnescapeLiteral(rule.Groups["value"].Value),
                Days = FromEffective(int.Parse(rule.Groups["days"].Value, CultureInfo.InvariantCulture)),
            });
        }

        // Every branch must have been consumed as a rule, or this isn't an expression Flare wrote.
        var consumed = RuleBranch().Replace(match.Groups["branches"].Value, "");
        return consumed.Length == 0
            ? (rules, FromEffective(int.Parse(match.Groups["default"].Value, CultureInfo.InvariantCulture)))
            : null;
    }

    private static int Effective(int days) => days <= 0 ? RetentionSql.ForeverDays : days;

    private static int FromEffective(int days) => days >= RetentionSql.ForeverDays ? 0 : days;

    /// <summary>String-literal escaping for ClickHouse: backslash and single quote.</summary>
    internal static string EscapeLiteral(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal);

    private static string UnescapeLiteral(string value) => Regex.Replace(value, @"\\(.)", "$1");

    [GeneratedRegex(@"^multiIf\((?<branches>.*?)(?<default>\d+)\)$", RegexOptions.CultureInvariant | RegexOptions.Singleline)]
    private static partial Regex MultiIf();

    /// <summary>One <c>(ResourceAttributes['k']) = 'v', N, </c> branch; ClickHouse parenthesises the map access when it prints the expression.</summary>
    [GeneratedRegex(@"\(?ResourceAttributes\['(?<key>[^']+)'\]\)? = '(?<value>(?:[^'\\]|\\.)*)', (?<days>\d+), ", RegexOptions.CultureInvariant)]
    private static partial Regex RuleBranch();
}
