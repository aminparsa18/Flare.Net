using Flare.Identity;

namespace Flare.Api.Auth;

/// <summary>
/// <c>Cors:AllowedOrigins</c>, read and normalized once at startup (<see cref="BrowserOrigins"/>): a configured
/// <c>https://flare.example.com/</c> or <c>https://example.com/flare</c> matches the browser's bare <c>Origin</c>.
/// Shared by the default CORS policy, the status-page CORS policy, the WebSocket origin guard and the SSO
/// return-URL check, so all of them agree on what is allowed.
/// </summary>
public sealed class CorsAllowedOrigins(IReadOnlyList<string> origins)
{
    public const string ConfigKey = "Cors:AllowedOrigins";

    public IReadOnlyList<string> Origins { get; } = origins;

    /// <summary>Normalizes <c>Cors:AllowedOrigins</c>; throws naming the entry for an invalid value or <c>*</c>
    /// (the default policy allows credentials, which the CORS spec forbids with a wildcard).</summary>
    public static CorsAllowedOrigins FromConfiguration(IConfiguration configuration) =>
        new(BrowserOrigins.NormalizeConfigured(
            configuration.GetSection(ConfigKey).Get<string[]>(), ConfigKey, allowWildcard: false));
}
