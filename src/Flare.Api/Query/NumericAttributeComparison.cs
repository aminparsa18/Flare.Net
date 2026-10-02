using System.Globalization;
using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;

namespace Flare.Api.Query;

/// <summary>
/// Shared pieces of the numeric attribute operators (<c>GreaterThan</c>/<c>GreaterThanOrEqual</c>/
/// <c>LessThan</c>/<c>LessThanOrEqual</c>) so the log and span SQL builders and
/// <see cref="LogFilterMatcher"/> agree on what counts as a number. Attribute values are stored
/// as strings, so the SQL side compiles to <c>toFloat64OrNull(value) &lt;op&gt; {p:Float64}</c> -
/// a non-numeric (or missing, <c>''</c>) value is NULL and never matches, and numbers compare
/// numerically rather than lexicographically.
/// </summary>
internal static class NumericAttributeComparison
{
    /// <summary>Parses a filter operand or attribute value; false for blank, non-numeric, or NaN input.</summary>
    public static bool TryParse(string? text, out double number) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number) && !double.IsNaN(number);

    public static bool IsNumber(string? text) => TryParse(text, out _);

    /// <summary>
    /// SQL for <c>toFloat64OrNull(<paramref name="valueSql"/>) op {param}</c> wrapped in
    /// <c>ifNull(..., 0)</c> so a NULL comparison is a plain false in any boolean context. An
    /// unparseable operand binds nothing and compiles to a constant false (matches nothing).
    /// </summary>
    public static string Clause(string valueSql, string symbol, string operand, string paramName, ClickHouseParameterCollection parameters)
    {
        if (!TryParse(operand, out var number))
        {
            return "0";
        }

        parameters.AddParameter(paramName, number);
        return $"ifNull(toFloat64OrNull({valueSql}) {symbol} {{{paramName}:Float64}}, 0)";
    }

    /// <summary>In-memory twin of <see cref="Clause"/> for live-tail matching.</summary>
    public static bool Matches(string? value, string operand, Func<int, bool> accept)
    {
        if (!TryParse(operand, out var rhs) || !TryParse(value, out var lhs))
        {
            return false;
        }

        return accept(lhs.CompareTo(rhs));
    }
}
