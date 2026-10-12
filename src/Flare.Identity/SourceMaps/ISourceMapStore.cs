namespace Flare.Identity.SourceMaps;

/// <summary>Metadata of one stored source map; <see cref="SizeBytes"/> is the uncompressed size.</summary>
public sealed record SourceMapInfo(string ServiceName, string Version, string Bundle, long SizeBytes, DateTimeOffset UploadedAt);

/// <summary>
/// Uploaded JavaScript source maps (ADR-0152), keyed by (service, version, bundle) where
/// <c>Bundle</c> is the served script's path, e.g. <c>assets/index-abc123.js</c>.
/// </summary>
public interface ISourceMapStore
{
    /// <summary>Stores or replaces a map. <paramref name="gzipContent"/> is the gzipped map JSON.</summary>
    Task UpsertAsync(string serviceName, string version, string bundle, byte[] gzipContent, long sizeBytes, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SourceMapInfo>> ListAsync(string? serviceName, string? version, CancellationToken cancellationToken = default);

    /// <summary>Deletes every map of the version, or only <paramref name="bundle"/> when given. Returns the row count.</summary>
    Task<int> DeleteAsync(string serviceName, string version, string? bundle, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListBundlesAsync(string serviceName, string version, CancellationToken cancellationToken = default);

    /// <summary>The gzipped map JSON, or null when absent.</summary>
    Task<(byte[] GzipContent, DateTimeOffset UploadedAt)?> GetAsync(string serviceName, string version, string bundle, CancellationToken cancellationToken = default);
}
