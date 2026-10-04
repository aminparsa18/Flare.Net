using System.Text.RegularExpressions;

namespace Flare.Api.Ai;

/// <summary>
/// Strips values that look like credentials or personal data from text before it is sent to an
/// LLM (ADR-0103). Best-effort pattern matching, not a guarantee: it targets what commonly leaks
/// into exception messages and source (tokens, passwords, connection strings, emails, IPs).
/// Pure and unit-tested.
/// </summary>
public static partial class AiRedactor
{
    public const string Mask = "[REDACTED]";

    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        text = PrivateKeyRegex().Replace(text, Mask);
        text = JwtRegex().Replace(text, Mask);
        text = BearerRegex().Replace(text, "$1" + Mask);
        text = UrlUserInfoRegex().Replace(text, "$1" + Mask + "@");
        text = SecretAssignmentRegex().Replace(text, "$1$2" + Mask);
        text = EmailRegex().Replace(text, Mask);
        text = Ipv4Regex().Replace(text, Mask);
        text = LongTokenRegex().Replace(text, Mask);
        return text;
    }

    [GeneratedRegex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----.*?(-----END [A-Z ]*PRIVATE KEY-----|$)", RegexOptions.Singleline)]
    private static partial Regex PrivateKeyRegex();

    [GeneratedRegex(@"\beyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*")]
    private static partial Regex JwtRegex();

    [GeneratedRegex(@"(\b(?:Bearer|Basic)\s+)[A-Za-z0-9._~+/=-]{8,}", RegexOptions.IgnoreCase)]
    private static partial Regex BearerRegex();

    [GeneratedRegex(@"(\b[a-z][a-z0-9+.-]*://)[^\s/@:]+(?::[^\s/@]*)?@", RegexOptions.IgnoreCase)]
    private static partial Regex UrlUserInfoRegex();

    // key=value / key: value / "key": "value" for secret-ish keys, incl. connection-string members.
    [GeneratedRegex(
        @"((?:pass(?:word|wd)?|pwd|secret|token|api[_-]?key|apikey|access[_-]?key|client[_-]?secret|authorization|auth|private[_-]?key|connection[_-]?string|sas|signature)[""']?\s*)([:=]\s*[""']?)[^\s;,""'&)}]+",
        RegexOptions.IgnoreCase)]
    private static partial Regex SecretAssignmentRegex();

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b")]
    private static partial Regex Ipv4Regex();

    // Long unbroken hex/base64-ish runs (API keys, hashes). Paths and identifiers contain '/', '.', or
    // '_' too often to match at 32+; 32+ contiguous alphanumerics with at least one digit is a key.
    [GeneratedRegex(@"\b(?=[A-Za-z0-9+/=_-]*\d)[A-Za-z0-9+/=_-]{32,}\b")]
    private static partial Regex LongTokenRegex();
}
