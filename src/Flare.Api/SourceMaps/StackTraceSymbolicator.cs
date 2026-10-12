using System.IO.Compression;
using Flare.Identity.SourceMaps;
using Flare.Mcp.DotnetSymbols;
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

        var (afterBrowser, browser) = await SymbolicateBrowserAsync(serviceName, versions, stacktrace, cancellationToken);
        var (afterDotnet, dotnet) = await SymbolicateDotnetAsync(serviceName, versions, afterBrowser, cancellationToken);
        var (afterNative, native) = await SymbolicateNativeAotAsync(serviceName, versions, afterDotnet, cancellationToken);
        return (afterNative, browser || dotnet || native);
    }

    private async Task<(string Stacktrace, bool Symbolicated)> SymbolicateBrowserAsync(string serviceName, IReadOnlyList<string> versions, string stacktrace, CancellationToken cancellationToken)
    {
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

    /// <summary>Mono frames (trimmed/AOT MAUI builds): one uploaded symbols file per assembly MVID.</summary>
    private async Task<(string Stacktrace, bool Symbolicated)> SymbolicateDotnetAsync(string serviceName, IReadOnlyList<string> versions, string stacktrace, CancellationToken cancellationToken)
    {
        var symbols = new Dictionary<Guid, DotnetSymbols?>();
        foreach (var mvid in MonoStackTrace.Frames(stacktrace).Select(f => f.Mvid).Distinct())
        {
            symbols[mvid] = await FindSymbolsAsync(serviceName, versions, mvid, cancellationToken);
        }

        return symbols.Count == 0
            ? (stacktrace, false)
            : MonoStackTrace.Rewrite(stacktrace, frame =>
                symbols.GetValueOrDefault(frame.Mvid)?.Lookup(frame.Method, frame.ParameterNames, frame.IlOffset));
    }

    /// <summary>
    /// Native AOT frames carry no image id, so every native symbols file uploaded for the release is tried;
    /// a frame resolves when exactly one file knows it (a release normally has one per architecture).
    /// </summary>
    private async Task<(string Stacktrace, bool Symbolicated)> SymbolicateNativeAotAsync(string serviceName, IReadOnlyList<string> versions, string stacktrace, CancellationToken cancellationToken)
    {
        if (!NativeAotStackTrace.HasFrames(stacktrace))
        {
            return (stacktrace, false);
        }

        var files = new List<NativeSymbols>();
        foreach (var version in versions.Where(v => v.Length > 0).Distinct())
        {
            foreach (var bundle in (await store.ListBundlesAsync(serviceName, version, cancellationToken)).Where(b => b.EndsWith(NativeSymbols.BundleSuffix, StringComparison.Ordinal)))
            {
                if (await LoadCachedAsync(serviceName, version, bundle, NativeSymbols.Parse, cancellationToken) is { } symbols)
                {
                    files.Add(symbols);
                }
            }

            if (files.Count > 0)
            {
                break;
            }
        }

        return files.Count == 0
            ? (stacktrace, false)
            : NativeAotStackTrace.Rewrite(stacktrace, frame =>
            {
                var hits = files.Select(f => f.Lookup(frame.Method, frame.Arguments, frame.Offset)).Where(h => h is not null).Distinct().ToList();
                return hits.Count == 1 ? hits[0] : null;
            });
    }

    private async Task<DotnetSymbols?> FindSymbolsAsync(string serviceName, IReadOnlyList<string> versions, Guid mvid, CancellationToken cancellationToken)
    {
        var bundle = DotnetSymbols.BundleName(mvid);
        foreach (var version in versions.Where(v => v.Length > 0).Distinct())
        {
            if (await LoadCachedAsync(serviceName, version, bundle, DotnetSymbols.Parse, cancellationToken) is { } found)
            {
                return found;
            }
        }

        return null;
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

            if (await LoadCachedAsync(serviceName, version, bundle, SourceMap.Parse, cancellationToken) is { } map)
            {
                return map;
            }
        }

        return null;
    }

    private async Task<T?> LoadCachedAsync<T>(string serviceName, string version, string bundle, ParseMap<T> parse, CancellationToken cancellationToken) where T : class
    {
        var stored = await store.GetAsync(serviceName, version, bundle, cancellationToken);
        if (stored is null)
        {
            return null;
        }

        var key = $"{serviceName}\n{version}\n{bundle}\n{stored.Value.UploadedAt:O}";
        if (_cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            using var gzip = new GZipStream(new MemoryStream(stored.Value.GzipContent), CompressionMode.Decompress);
            using var json = new MemoryStream();
            await gzip.CopyToAsync(json, cancellationToken);
            var parsed = parse(json.GetBuffer().AsSpan(0, (int)json.Length));
            _cache.Set(key, parsed, new MemoryCacheEntryOptions { Size = json.Length, SlidingExpiration = TimeSpan.FromMinutes(10) });
            return parsed;
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            // A stored file that no longer parses just leaves its frames unresolved.
            return null;
        }
    }

    private delegate T ParseMap<out T>(ReadOnlySpan<byte> json);

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
