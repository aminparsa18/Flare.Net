using System.Net.Http.Headers;
using System.Text;
using Flare.Identity.SourceLinks;

namespace Flare.Api.Source;

/// <summary>
/// Builds the repo-host API requests behind suspect commits (docs-internal/adr/0263-suspect-commits.md):
/// the commits between two refs, and the files one commit changed. Pure, so the three hosts' URL, body and
/// auth shapes are unit-tested without HTTP. Same host conventions as <see cref="SourceFetchRequestBuilder"/>.
/// </summary>
public static class SuspectCommitRequestBuilder
{
    /// <summary>Commits reachable from <paramref name="headRef"/> but not <paramref name="baseRef"/>, at most <see cref="MaxCommits"/>.</summary>
    public const int MaxCommits = 100;

    public static HttpRequestMessage? BuildCompare(SourceLinkConfig config, string baseRef, string headRef)
    {
        if (!TryParse(config, out var repo, out var segments))
        {
            return null;
        }

        switch (config.Provider)
        {
            case SourceLinkProvider.GitHub when segments.Length >= 2:
                return GitHub(config, $"{GitHubApi(repo)}/repos/{segments[0]}/{segments[1]}/compare/{Uri.EscapeDataString(baseRef)}...{Uri.EscapeDataString(headRef)}?per_page={MaxCommits}");
            case SourceLinkProvider.GitLab when segments.Length >= 2:
                return GitLab(config, $"{repo.Scheme}://{repo.Authority}/api/v4/projects/{Uri.EscapeDataString(string.Join('/', segments))}/repository/compare?from={Uri.EscapeDataString(baseRef)}&to={Uri.EscapeDataString(headRef)}");
            case SourceLinkProvider.AzureDevOps when IsAzure(segments):
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{AzureRepoApi(repo, segments)}/commitsbatch?$top={MaxCommits}&api-version=7.1");
                var body = "{\"itemVersion\":{" + AzureVersion(headRef) + "},\"compareVersion\":{" + AzureVersion(baseRef) + "}}";
                request.Content = new StringContent(body, Encoding.UTF8, new MediaTypeHeaderValue("application/json"));
                return Azure(config, request);
            }
            default:
                return null;
        }
    }

    /// <summary>The files changed by one commit.</summary>
    public static HttpRequestMessage? BuildCommitFiles(SourceLinkConfig config, string sha)
    {
        if (!TryParse(config, out var repo, out var segments))
        {
            return null;
        }

        switch (config.Provider)
        {
            case SourceLinkProvider.GitHub when segments.Length >= 2:
                return GitHub(config, $"{GitHubApi(repo)}/repos/{segments[0]}/{segments[1]}/commits/{Uri.EscapeDataString(sha)}");
            case SourceLinkProvider.GitLab when segments.Length >= 2:
                return GitLab(config, $"{repo.Scheme}://{repo.Authority}/api/v4/projects/{Uri.EscapeDataString(string.Join('/', segments))}/repository/commits/{Uri.EscapeDataString(sha)}/diff?per_page=100");
            case SourceLinkProvider.AzureDevOps when IsAzure(segments):
                return Azure(config, new HttpRequestMessage(HttpMethod.Get, $"{AzureRepoApi(repo, segments)}/commits/{Uri.EscapeDataString(sha)}/changes?api-version=7.1"));
            default:
                return null;
        }
    }

    /// <summary>The host's web page for one commit, shown as the link on a suspect.</summary>
    public static string CommitUrl(SourceLinkConfig config, string sha)
    {
        var repo = config.RepoUrl.TrimEnd('/');
        return config.Provider == SourceLinkProvider.GitLab ? $"{repo}/-/commit/{sha}" : $"{repo}/commit/{sha}";
    }

    /// <summary>The host's web page comparing the two refs.</summary>
    public static string CompareUrl(SourceLinkConfig config, string baseRef, string headRef)
    {
        var repo = config.RepoUrl.TrimEnd('/');
        return config.Provider switch
        {
            SourceLinkProvider.GitLab => $"{repo}/-/compare/{Uri.EscapeDataString(baseRef)}...{Uri.EscapeDataString(headRef)}",
            SourceLinkProvider.AzureDevOps => $"{repo}/branchCompare?baseVersion={AzurePrefix(baseRef)}{Uri.EscapeDataString(baseRef)}&targetVersion={AzurePrefix(headRef)}{Uri.EscapeDataString(headRef)}",
            _ => $"{repo}/compare/{Uri.EscapeDataString(baseRef)}...{Uri.EscapeDataString(headRef)}",
        };
    }

    private static bool TryParse(SourceLinkConfig config, out Uri repo, out string[] segments)
    {
        segments = [];
        if (!Uri.TryCreate(config.RepoUrl, UriKind.Absolute, out var parsed))
        {
            repo = null!;
            return false;
        }

        repo = parsed;
        segments = repo.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return true;
    }

    private static string GitHubApi(Uri repo) =>
        repo.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ? "https://api.github.com" : $"{repo.Scheme}://{repo.Authority}/api/v3";

    private static bool IsAzure(string[] segments) => segments.Length >= 4 && segments[^2] == "_git";

    private static string AzureRepoApi(Uri repo, string[] segments) =>
        $"{repo.Scheme}://{repo.Authority}/{string.Join('/', segments[..^2].Select(Uri.EscapeDataString))}/_apis/git/repositories/{Uri.EscapeDataString(segments[^1])}";

    private static bool IsCommitSha(string gitRef) => gitRef.Length is >= 7 and <= 40 && gitRef.All(Uri.IsHexDigit);

    private static string AzurePrefix(string gitRef) => IsCommitSha(gitRef) ? "GC" : "GB";

    private static string AzureVersion(string gitRef) => $"\"version\":\"{gitRef}\",\"versionType\":\"{(IsCommitSha(gitRef) ? "commit" : "branch")}\"";

    private static HttpRequestMessage GitHub(SourceLinkConfig config, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        request.Headers.TryAddWithoutValidation("User-Agent", "Flare");
        if (!string.IsNullOrEmpty(config.AccessToken))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {config.AccessToken}");
        }

        return request;
    }

    private static HttpRequestMessage GitLab(SourceLinkConfig config, string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(config.AccessToken))
        {
            request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", config.AccessToken);
        }

        return request;
    }

    private static HttpRequestMessage Azure(SourceLinkConfig config, HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(config.AccessToken))
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Basic {Convert.ToBase64String(Encoding.UTF8.GetBytes(":" + config.AccessToken))}");
        }

        return request;
    }
}
