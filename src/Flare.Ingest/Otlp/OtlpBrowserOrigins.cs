using Flare.Identity;

namespace Flare.Ingest.Otlp;

/// <summary>
/// <see cref="OtlpReceiverOptions.AllowedOrigins"/>, normalized once at startup (<see cref="BrowserOrigins"/>): a
/// configured <c>https://app.example.com/</c> matches the browser's bare <c>Origin</c>, <c>*</c> is kept, and an
/// invalid entry fails startup naming the key. Used by the browser CORS policy together with the per-key origins.
/// </summary>
public sealed class OtlpBrowserOrigins(IReadOnlyList<string> origins)
{
    public const string ConfigKey = OtlpReceiverOptions.SectionName + ":" + nameof(OtlpReceiverOptions.AllowedOrigins);

    public IReadOnlyList<string> Origins { get; } = origins;

    public static OtlpBrowserOrigins FromOptions(OtlpReceiverOptions options) =>
        new(BrowserOrigins.NormalizeConfigured(options.AllowedOrigins, ConfigKey, allowWildcard: true));

    /// <summary>True if <paramref name="origin"/> (a browser <c>Origin</c> header) is listed or <c>*</c> is. The CORS
    /// policy also allows any origin an active ingest key lists (ADR-0149).</summary>
    public bool IsConfigured(string origin) =>
        Origins.Contains(BrowserOrigins.Wildcard) || Origins.Contains(origin, StringComparer.OrdinalIgnoreCase);
}
