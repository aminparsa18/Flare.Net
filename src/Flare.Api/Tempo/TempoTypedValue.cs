using System.Globalization;
using System.Text.Json;

namespace Flare.Api.Tempo;

public enum TempoValueKind
{
    String,
    Int,
    Double,
    Bool,
}

/// <summary>An attribute value as the Tempo API writes it: the kind the stored text is unambiguously, and that value.</summary>
public readonly record struct TempoTypedValue(TempoValueKind Kind, string Text, long Integer = 0, double Number = 0, bool Boolean = false)
{
    /// <summary>
    /// Flare stores every attribute value as text, so the type an OTLP source gave it is gone (ADR-0230). The one rule: a value is a
    /// number or a bool only when writing that number or bool back gives exactly the stored text, so nothing is lost and no id,
    /// version or code is turned into a number it was not.
    /// <list type="bullet">
    /// <item><description><c>true</c> and <c>false</c> (lower case, nothing around them) are bools.</description></item>
    /// <item><description>A whole number in the range of a 64-bit integer written in its canonical form (an optional <c>-</c>, no <c>+</c>, no leading zero, not <c>-0</c>) is an int: <c>200</c>, <c>-3</c>, <c>0</c>.</description></item>
    /// <item><description>A finite number with a fraction or an exponent that prints back as the same text with .NET's shortest round-trip format is a double: <c>0.25</c>, <c>1E+21</c>.</description></item>
    /// <item><description>Everything else stays text: <c>0123</c>, <c>007</c>, <c>+5</c>, <c>5.0</c>, <c>1e5</c>, <c>-0</c>, <c> 5</c>, <c>NaN</c>, <c>1_000</c>, a number beyond 64 bits.</description></item>
    /// </list>
    /// A value that is only digits, such as an order number, is an int: the rule cannot tell it from a count.
    /// </summary>
    public static TempoTypedValue Infer(string text)
    {
        if (text is "true" or "false")
        {
            return new TempoTypedValue(TempoValueKind.Bool, text, Boolean: text == "true");
        }

        if (text.Length is 0 or > 40)
        {
            return new TempoTypedValue(TempoValueKind.String, text);
        }

        if (long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var integer)
            && integer.ToString(CultureInfo.InvariantCulture) == text)
        {
            return new TempoTypedValue(TempoValueKind.Int, text, Integer: integer);
        }

        if ((text.Contains('.') || text.Contains('E'))
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && double.IsFinite(number)
            && number.ToString("R", CultureInfo.InvariantCulture) == text)
        {
            return new TempoTypedValue(TempoValueKind.Double, text, Number: number);
        }

        return new TempoTypedValue(TempoValueKind.String, text);
    }

    /// <summary>
    /// Writes the one member of an AnyValue object: <c>intValue</c> (a string, as protobuf JSON writes an int64), <c>doubleValue</c>,
    /// <c>boolValue</c> or <c>stringValue</c>. With <paramref name="infer"/> false the text is always a string.
    /// </summary>
    public static void WriteMember(Utf8JsonWriter w, string text, bool infer)
    {
        var value = infer ? Infer(text) : new TempoTypedValue(TempoValueKind.String, text);
        switch (value.Kind)
        {
            case TempoValueKind.Int:
                w.WriteString("intValue", value.Integer.ToString(CultureInfo.InvariantCulture));
                break;
            case TempoValueKind.Double:
                w.WriteNumber("doubleValue", value.Number);
                break;
            case TempoValueKind.Bool:
                w.WriteBoolean("boolValue", value.Boolean);
                break;
            default:
                w.WriteString("stringValue", text);
                break;
        }
    }
}
