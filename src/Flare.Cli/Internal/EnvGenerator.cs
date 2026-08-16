using System.Security.Cryptography;

namespace Flare.Cli.Internal;

/// <summary>
/// Generates the random credentials written into <c>~/.flare/.env</c> on first
/// <c>flare start</c>. Deliberately NOT the repo's own docker-compose.yml
/// <c>flare</c>/<c>flare</c> default: this instance is framed as standing (may run for
/// weeks with its ports bound on the host the whole time), not a throwaway dev
/// container torn down after a quick eval, so a known, documented public password isn't
/// an acceptable default here. See docs/cli.md.
/// </summary>
internal static class EnvGenerator
{
    // Alphanumeric only - these values get interpolated into both .env and compose's
    // CMD-SHELL healthcheck strings (see Templates/docker-compose.flare.yml), so no
    // shell-metacharacter risk is the deliberate choice, not an oversight.
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    public static string RenderEnvTemplate(string template)
    {
        return template
            .Replace("{{CLICKHOUSE_PASSWORD}}", GenerateRandomSecret())
            .Replace("{{REDIS_PASSWORD}}", GenerateRandomSecret());
    }

    private static string GenerateRandomSecret(int length = 32)
    {
        Span<char> chars = stackalloc char[length];
        for (var i = 0; i < length; i++)
        {
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }

        return new string(chars);
    }
}
