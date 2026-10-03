using System.Text;
using Flare.Api.Model;
using Flare.Identity.SourceLinks;
using Microsoft.Extensions.Caching.Memory;

namespace Flare.Api.Source;

public interface ISourceSnippetService
{
    /// <summary>Lines around the requested one, or a user-facing error. Never throws for host/validation failures.</summary>
    Task<(SourceSnippetResponse? Snippet, string? Error)> GetAsync(SourceSnippetRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Fetches a source file from the service's configured repo host and returns a few lines around
/// a frame - see docs-internal/adr/0096-inline-exception-source.md. Bounded on every axis: no
/// redirects (the token must not follow one off-host), 10s timeout, 2 MB body cap, 10 minute cache
/// (a commit's content never changes; a branch's barely matters for a snippet).
/// </summary>
public sealed class SourceSnippetService(
    IHttpClientFactory httpClientFactory,
    ISourceLinkStore store) : ISourceSnippetService
{
    // Private, size-bounded (in lines) cache - the app-wide IMemoryCache has no limit.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 200_000 });

    public const string HttpClientName = "source-fetch";
    public const int ContextLines = 6;
    private const int MaxBodyBytes = 2 * 1024 * 1024;
    private const int MaxLineLength = 400;

    public async Task<(SourceSnippetResponse? Snippet, string? Error)> GetAsync(SourceSnippetRequest request, CancellationToken cancellationToken)
    {
        if (request.Line < 1 || !IsSafePath(request.Path) || !IsSafeRef(request.Ref))
        {
            return (null, "Invalid path, ref, or line.");
        }

        var config = (await store.GetAllAsync(cancellationToken)).FirstOrDefault(c => c.ServiceName == request.ServiceName);
        if (config is null)
        {
            return (null, "No source repository is configured for this service.");
        }

        var cacheKey = $"source|{config.RepoUrl}|{request.Ref}|{request.Path}";
        if (!_cache.TryGetValue(cacheKey, out string[]? lines) || lines is null)
        {
            using var message = SourceFetchRequestBuilder.Build(config, request.Ref, request.IsCommit, request.Path);
            if (message is null)
            {
                return (null, "The configured repository URL doesn't match the selected host.");
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    return (null, $"The repository host returned {(int)response.StatusCode}. For a private repository, add an access token.");
                }

                var text = await ReadCappedAsync(response, timeout.Token);
                if (text is null)
                {
                    return (null, "The file is too large to preview.");
                }

                lines = text.Replace("\r\n", "\n").Split('\n');
                _cache.Set(cacheKey, lines, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10), Size = lines.Length });
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                return (null, "Could not reach the repository host.");
            }
        }

        if (request.Line > lines.Length)
        {
            return (null, "The file at this commit has fewer lines than the frame claims.");
        }

        var start = Math.Max(1, request.Line - ContextLines);
        var end = Math.Min(lines.Length, request.Line + ContextLines);
        return (new SourceSnippetResponse
        {
            StartLine = start,
            Lines = [.. lines[(start - 1)..end].Select(l => l.Length > MaxLineLength ? l[..MaxLineLength] : l)]
        }, null);
    }

    public static bool IsSafePath(string path) =>
        path.Length is > 0 and <= 500 && !path.Contains('\\') && !path.StartsWith('/') && !path.Split('/').Any(s => s is "" or "." or "..");

    public static bool IsSafeRef(string gitRef) =>
        gitRef.Length is > 0 and <= 200 && gitRef.All(c => char.IsLetterOrDigit(c) || c is '.' or '/' or '_' or '-' or '+');

    private static async Task<string?> ReadCappedAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxBodyBytes)
            {
                return null;
            }
        }

        return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }
}
