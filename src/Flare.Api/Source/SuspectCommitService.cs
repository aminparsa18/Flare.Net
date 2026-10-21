using Flare.Api.Model;
using Flare.Identity.SourceLinks;
using Microsoft.Extensions.Caching.Memory;

namespace Flare.Api.Source;

public interface ISuspectCommitService
{
    /// <summary>The commits of the range that touched the stack's files, or a user-facing error. Never throws for host/validation failures.</summary>
    Task<(SuspectCommitsResponse? Result, string? Error)> GetAsync(SuspectCommitsRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Lists the commits between two releases of a service's repo and keeps the ones that changed a file in a
/// failing stack - see docs-internal/adr/0263-suspect-commits.md. Same posture as <see cref="SourceSnippetService"/>
/// (shared guarded HTTP client, no redirects, the service's own token, bounded body): the host is asked for the
/// range once, and for a commit's files only when the range's own file list can't rule it out, at most
/// <see cref="MaxInspected"/> commits, a few at a time.
/// </summary>
public sealed class SuspectCommitService(
    IHttpClientFactory httpClientFactory,
    ISourceLinkStore store) : ISuspectCommitService
{
    public const int MaxInspected = 40;
    private const int MaxBodyBytes = 4 * 1024 * 1024;
    private const int Parallelism = 4;
    private const int MaxSuspects = 5;

    // Refs are release commits, which never change, so the answer for the same inputs is reusable.
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 500 });

    public async Task<(SuspectCommitsResponse? Result, string? Error)> GetAsync(SuspectCommitsRequest request, CancellationToken cancellationToken)
    {
        var paths = request.Paths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count is 0 or > SuspectCommitsRequest.MaxPaths || !paths.All(SourceSnippetService.IsSafePath)
            || !SourceSnippetService.IsSafeRef(request.BaseRef) || !SourceSnippetService.IsSafeRef(request.HeadRef))
        {
            return (null, "Invalid ref or path.");
        }

        var config = (await store.GetAllAsync(cancellationToken)).FirstOrDefault(c => c.ServiceName == request.ServiceName);
        if (config is null)
        {
            return (null, "No source repository is configured for this service.");
        }

        var compareUrl = SuspectCommitRequestBuilder.CompareUrl(config, request.BaseRef, request.HeadRef);
        var cacheKey = $"suspects|{config.RepoUrl}|{request.BaseRef}|{request.HeadRef}|{string.Join('|', paths)}";
        if (_cache.TryGetValue(cacheKey, out SuspectCommitsResponse? cached) && cached is not null)
        {
            return (cached, null);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));

            var (compare, error) = await FetchAsync<HostCompare>(config, SuspectCommitRequestBuilder.BuildCompare(config, request.BaseRef, request.HeadRef), SuspectCommitParser.ParseCompare, timeout.Token);
            if (compare is null)
            {
                return (null, error);
            }

            var commits = compare.Commits.OrderByDescending(c => c.Date).ToList();
            var inspect = compare.ChangedPaths is not null && !paths.Any(compare.ChangedPaths.Contains)
                ? []
                : commits.Take(MaxInspected).ToList();

            var changedBySha = new Dictionary<string, IReadOnlySet<string>>();
            string? firstError = null;
            await Parallel.ForEachAsync(inspect, new ParallelOptions { MaxDegreeOfParallelism = Parallelism, CancellationToken = timeout.Token }, async (commit, token) =>
            {
                var (files, fileError) = await FetchAsync<IReadOnlySet<string>>(config, SuspectCommitRequestBuilder.BuildCommitFiles(config, commit.Sha), SuspectCommitParser.ParseCommitPaths, token);
                lock (changedBySha)
                {
                    if (files is not null)
                    {
                        changedBySha[commit.Sha] = files;
                    }
                    else
                    {
                        firstError ??= fileError;
                    }
                }
            });

            if (changedBySha.Count == 0 && inspect.Count > 0)
            {
                return (null, firstError);
            }

            var suspects = SuspectCommitParser.Rank(commits, changedBySha, paths)
                .Take(MaxSuspects)
                .Select(r => new SuspectCommit
                {
                    Sha = r.Commit.Sha,
                    Message = r.Commit.Message,
                    Author = r.Commit.Author,
                    Date = r.Commit.Date,
                    Url = SuspectCommitRequestBuilder.CommitUrl(config, r.Commit.Sha),
                    MatchedPaths = r.Matched,
                })
                .ToList();

            var result = new SuspectCommitsResponse
            {
                Commits = suspects,
                TotalCommits = Math.Max(compare.Total, compare.Commits.Count),
                Truncated = inspect.Count > 0 && (inspect.Count < compare.Commits.Count || compare.Total > compare.Commits.Count),
                CompareUrl = compareUrl,
            };
            _cache.Set(cacheKey, result, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10), Size = 1 });
            return (result, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            return (null, "Could not read the repository host's response.");
        }
    }

    private async Task<(T? Value, string? Error)> FetchAsync<T>(SourceLinkConfig config, HttpRequestMessage? message, Func<SourceLinkProvider, string, T> parse, CancellationToken cancellationToken)
        where T : class
    {
        if (message is null)
        {
            return (null, "The configured repository URL doesn't match the selected host.");
        }

        using (message)
        {
            using var response = await httpClientFactory.CreateClient(SourceSnippetService.HttpClientName).SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return (null, $"The repository host returned {(int)response.StatusCode}. For a private repository, add an access token; an unknown commit also returns 404.");
            }

            var text = await SourceSnippetService.ReadCappedAsync(response, MaxBodyBytes, cancellationToken);
            return text is null ? (null, "The repository host's response is too large.") : (parse(config.Provider, text), null);
        }
    }
}
