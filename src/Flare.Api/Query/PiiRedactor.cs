using System.Text;
using System.Text.RegularExpressions;

namespace Flare.Api.Query;

/// <summary>Which built-in PII/secret detectors <see cref="PiiRedactor"/> runs (ADR-0199). Bound from <c>PipelineRules:Pii:Detectors</c> as a comma-separated list, e.g. <c>Email,CreditCard</c>.</summary>
[Flags]
public enum PiiDetectors
{
    None = 0,
    Email = 1,
    CreditCard = 2,
    Iban = 4,
    Jwt = 8,
    BearerToken = 16,
    PrivateKey = 32,
    All = Email | CreditCard | Iban | Jwt | BearerToken | PrivateKey,
}

/// <summary>
/// Mirror of Flare.Ingest.Pipeline.Rules.PiiRedactor for the preview endpoint (ADR-0199, same mirror-not-reference boundary as ADR-0034). Pure managed PII/secret redaction applied at ingest. Each match is replaced with
/// <c>[REDACTED:kind]</c> so a reader can tell what was removed. Card numbers (Luhn) and IBANs
/// (ISO 7064 mod 97) are checksum-validated so order ids, timestamps and other digit runs aren't
/// masked. Best-effort pattern matching, not a guarantee. Every regex carries a match timeout; a
/// timeout leaves the text unchanged for that detector (fail open on that one detector, never throws).
/// </summary>
public static partial class PiiRedactor
{
    private const int TimeoutMs = 100;

    public static string Redact(string text, PiiDetectors detectors)
    {
        if (detectors == PiiDetectors.None || string.IsNullOrEmpty(text))
        {
            return text;
        }

        // Order matters: structured secrets first so a JWT's base64 can't be half-eaten by a later pattern.
        if (detectors.HasFlag(PiiDetectors.PrivateKey) && text.Contains("PRIVATE KEY", StringComparison.Ordinal))
        {
            text = Replace(text, PrivateKeyRegex(), static _ => "[REDACTED:private-key]");
        }

        if (detectors.HasFlag(PiiDetectors.Jwt) && text.Contains("eyJ", StringComparison.Ordinal))
        {
            text = Replace(text, JwtRegex(), static _ => "[REDACTED:jwt]");
        }

        if (detectors.HasFlag(PiiDetectors.BearerToken) && text.Contains("bearer", StringComparison.OrdinalIgnoreCase))
        {
            text = Replace(text, BearerRegex(), static m => m.Groups[1].Value + "[REDACTED:bearer-token]");
        }

        if (detectors.HasFlag(PiiDetectors.Email) && text.Contains('@'))
        {
            text = Replace(text, EmailRegex(), static _ => "[REDACTED:email]");
        }

        if (detectors.HasFlag(PiiDetectors.Iban))
        {
            text = Replace(text, IbanRegex(), static m => IsValidIban(m.Value) ? "[REDACTED:iban]" : m.Value);
        }

        if (detectors.HasFlag(PiiDetectors.CreditCard))
        {
            text = Replace(text, CardRegex(), static m => IsValidCard(m.Value) ? "[REDACTED:card]" : m.Value);
        }

        return text;
    }

    private static string Replace(string text, Regex regex, MatchEvaluator evaluator)
    {
        try
        {
            return regex.Replace(text, evaluator);
        }
        catch (RegexMatchTimeoutException)
        {
            return text;
        }
    }

    /// <summary>13-19 digits (spaces/dashes allowed between them) that pass the Luhn check.</summary>
    internal static bool IsValidCard(string candidate)
    {
        Span<int> digits = stackalloc int[19];
        var count = 0;
        foreach (var c in candidate)
        {
            if (c is ' ' or '-')
            {
                continue;
            }

            if (count == digits.Length)
            {
                return false;
            }

            digits[count++] = c - '0';
        }

        if (count < 13)
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < count; i++)
        {
            var d = digits[count - 1 - i];
            if (i % 2 == 1)
            {
                d *= 2;
                if (d > 9)
                {
                    d -= 9;
                }
            }

            sum += d;
        }

        return sum % 10 == 0;
    }

    /// <summary>15-34 chars, country code + check digits + BBAN, passing ISO 7064 mod 97-10.</summary>
    internal static bool IsValidIban(string candidate)
    {
        var compact = candidate.Replace(" ", "", StringComparison.Ordinal);
        if (compact.Length is < 15 or > 34)
        {
            return false;
        }

        var rearranged = compact[4..] + compact[..4];
        var remainder = 0;
        foreach (var c in rearranged)
        {
            if (c is >= '0' and <= '9')
            {
                remainder = (remainder * 10 + (c - '0')) % 97;
            }
            else if (c is >= 'A' and <= 'Z')
            {
                remainder = (remainder * 100 + (c - 'A' + 10)) % 97;
            }
            else
            {
                return false;
            }
        }

        return remainder == 1;
    }

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----.*?(-----END [A-Z ]*PRIVATE KEY-----|$)", RegexOptions.Singleline, TimeoutMs)]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*", RegexOptions.None, TimeoutMs)]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"(\bBearer\s+)[A-Za-z0-9._~+/=-]{8,}", RegexOptions.IgnoreCase, TimeoutMs)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}", RegexOptions.None, TimeoutMs)]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(?<![A-Za-z0-9])[A-Z]{2}\d{2}(?: ?[A-Z0-9]{4}){2,7}(?: ?[A-Z0-9]{1,3})?(?![A-Za-z0-9])", RegexOptions.None, TimeoutMs)]
    private static partial Regex IbanRegex();

    [GeneratedRegex(@"(?<![\d])\d(?:[ -]?\d){12,18}(?![\d])", RegexOptions.None, TimeoutMs)]
    private static partial Regex CardRegex();
}
