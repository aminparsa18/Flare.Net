using System.Globalization;
using System.Security.Cryptography;

namespace Flare.Cli.Internal;

/// <summary>
/// Generates the random credentials, and resolves the ports, written into a fresh
/// instance's <c>.env</c> on its first <c>flare start</c>. Passwords are deliberately NOT
/// the repo's own docker-compose.yml <c>flare</c>/<c>flare</c> default: this instance is
/// framed as standing (may run for weeks with its ports bound on the host the whole
/// time), not a throwaway dev container torn down after a quick eval, so a known,
/// documented public password isn't an acceptable default here. See docs/cli.md.
/// </summary>
internal static class EnvGenerator
{
    // Alphanumeric only - these values get interpolated into both .env and compose's
    // CMD-SHELL healthcheck strings (see Templates/docker-compose.flare.yml), so no
    // shell-metacharacter risk is the deliberate choice, not an oversight.
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    /// <summary>
    /// Renders env.template into a real .env: random passwords always, plus each
    /// service's port. <paramref name="portDefaults"/> is the target instance's own
    /// topology profile's port table (see <see cref="TopologyProfile.Ports"/>) -
    /// Standalone and Cluster carry different port sets, so this can no longer assume
    /// <see cref="PortDefaults.All"/> the way it used to. <paramref name="ports"/> is the
    /// named-instance auto-probe result (see <see cref="PortDefaults.ProbeFreePorts"/>) -
    /// omit it (the default instance's own path) to fall back to
    /// <paramref name="portDefaults"/>'s static defaults, which keeps the default
    /// instance's rendered .env byte-identical to before per-instance ports existed.
    /// PUBLIC_API_URL is derived from the resolved FLARE_API_PORT rather than templated
    /// as its own independent literal, so a named instance's dashboard always points its
    /// browser-side API calls at the port that instance actually uses.
    /// </summary>
    public static string RenderEnvTemplate(string template, (string Label, string EnvKey, int Fallback)[] portDefaults, IReadOnlyDictionary<string, int>? ports = null)
    {
        var resolved = portDefaults.ToDictionary(
            p => p.EnvKey,
            p => ports is not null && ports.TryGetValue(p.EnvKey, out var overridden) ? overridden : p.Fallback);

        var rendered = template
            .Replace("{{CLICKHOUSE_PASSWORD}}", GenerateRandomSecret())
            .Replace("{{REDIS_PASSWORD}}", GenerateRandomSecret());

        foreach (var (envKey, value) in resolved)
        {
            rendered = rendered.Replace($"{{{{{envKey}}}}}", value.ToString(CultureInfo.InvariantCulture));
        }

        rendered = rendered.Replace("{{PUBLIC_API_URL}}", $"http://localhost:{resolved["FLARE_API_PORT"]}");

        return rendered;
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
