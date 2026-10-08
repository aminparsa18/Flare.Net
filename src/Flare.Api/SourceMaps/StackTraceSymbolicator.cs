using System.IO.Compression;
using Flare.Identity.SourceMaps;
using Microsoft.Extensions.Caching.Memory;

namespace Flare.Api.SourceMaps;

public interface IStackTraceSymbolicator
{
    /// <summary>
    /// Rewrites browser frames of <paramref name="stacktrace"/> using the source maps uploaded for
    /// <paramref name="serviceName"/> at any of <paramref name="versions"/> (first match wins).
    /// Returns the input unchanged when nothing resolved.
    /// </summary>
    Task<(string Stacktrace, bool Symbolicated)> SymbolicateAsync(string serviceName, IReadOnlyList<string> versions, string stacktrace, CancellationToken cancellationToken);
}

/// <summary>
/// Matches each browser frame's script URL to an uploaded bundle (the longest bundle path that is a
/// suffix of the URL's path) and looks the position up in its parsed map. Parsed maps are cached
/// by upload time, so a re-upload takes effect on the next request. See ADR-0152.
/// </summary>
public sealed class StackTraceSymbolicator(ISourceMapStore store) : IStackTraceSymbolicator, IDisposable
{
    // Cost is the uncompressed map size, a rough proxy for the parsed segment arrays.
    private const long CacheBudgetBytes = 128L * 1024 * 1024;

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = CacheBudgetBytes });

    public async Task<(string Stacktrace, bool Symbolicated)> SymbolicateAsync(string serviceName, IReadOnlyList<string> versions, string stacktrace, CancellationToken cancellationToken)
    {
        if (stacktrace.Length == 0 || versions.Count == 0)
        {
            return (stacktrace, false);
        }

        var frames = new List<BrowserFrame>();
        foreach (var line in stacktrace.Split('\n'))
        {
            if (BrowserStackTrace.TryParseFrame(line.TrimEnd('\r'), out _, out var frame))
            {
                frames.Add(frame);
            }
        }

        if (frames.Count == 0)
        {
            return (stacktrace, false);
        }

        // Resolve asynchronously up front (the rewrite callback is synchronous): one map per distinct URL.
        var maps = new Dictionary<string, SourceMap?>();
        foreach (var url in frames.Select(f => f.Url).Distinct())
        {
            maps[url] = await FindMapAsync(serviceName, versions, url, cancellationToken);
        }

        return BrowserStackTrace.Rewrite(stacktrace, frame =>
            maps.GetValueOrDefault(frame.Url) is { } map ? map.Lookup(frame.Line - 1, frame.Column - 1) : null);
    }

    private async Task<SourceMap?> FindMapAsync(string serviceName, IReadOnlyList<string> versions, string url, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        foreach (var version in versions.Where(v => v.Length > 0).Distinct())
        {
            var bundle = BestBundle(await store.ListBundlesAsync(serviceName, version, cancellationToken), uri.AbsolutePath);
            if (bundle is null)
            {
                continue;
            }

            var stored = await store.GetAsync(serviceName, version, bundle, cancellationToken);
            if (stored is null)
            {
                continue;
            }

            var key = $"{serviceName}\n{version}\n{bundle}\n{stored.Value.UploadedAt:O}";
            if (_cache.TryGetValue(key, out SourceMap? cached) && cached is not null)
            {
                return cached;
            }

            try
            {
                using var gzip = new GZipStream(new MemoryStream(stored.Value.GzipContent), CompressionMode.Decompress);
                using var json = new MemoryStream();
                await gzip.CopyToAsync(json, cancellationToken);
                var map = SourceMap.Parse(json.GetBuffer().AsSpan(0, (int)json.Length));
                _cache.Set(key, map, new MemoryCacheEntryOptions { Size = json.Length, SlidingExpiration = TimeSpan.FromMinutes(10) });
                return map;
            }
            catch (Exception ex) when (ex is FormatException or InvalidDataException)
            {
                // A stored map that no longer parses just leaves its frames unresolved.
            }
        }

        return null;
    }

    /// <summary>The longest uploaded bundle path that the served script's path ends with.</summary>
    public static string? BestBundle(IEnumerable<string> bundles, string urlPath)
    {
        string? best = null;
        foreach (var bundle in bundles)
        {
            var normalized = bundle.Trim('/');
            if (normalized.Length == 0 || !urlPath.EndsWith("/" + normalized, StringComparison.Ordinal))
            {
                continue;
            }

            if (best is null || bundle.Length > best.Length)
            {
                best = bundle;
            }
        }

        return best;
    }

    public void Dispose() => _cache.Dispose();
}
