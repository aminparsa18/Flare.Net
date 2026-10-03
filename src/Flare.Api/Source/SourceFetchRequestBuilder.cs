using Flare.Identity.SourceLinks;

namespace Flare.Api.Source;

/// <summary>
/// Builds the repo-host API request that returns one file's raw content at a ref - pure, so
/// the three hosts' URL/auth shapes are unit-tested without HTTP. See
/// docs-internal/adr/0096-inline-exception-source.md.
/// </summary>
public static class SourceFetchRequestBuilder
{
    public static HttpRequestMessage? Build(SourceLinkConfig config, string gitRef, bool isCommit, string path)
    {
        if (!Uri.TryCreate(config.RepoUrl, UriKind.Absolute, out var repo))
        {
            return null;
        }

        var segments = repo.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var token = string.IsNullOrEmpty(config.AccessToken) ? null : config.AccessToken;
        var encodedPath = string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

        switch (config.Provider)
        {
            case SourceLinkProvider.GitHub when segments.Length >= 2:
            {
                var api = repo.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ? "https://api.github.com" : $"{repo.Scheme}://{repo.Authority}/api/v3";
                var request = new HttpRequestMessage(HttpMethod.Get, $"{api}/repos/{segments[0]}/{segments[1]}/contents/{encodedPath}?ref={Uri.EscapeDataString(gitRef)}");
                request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github.raw+json");
                request.Headers.TryAddWithoutValidation("User-Agent", "Flare");
                if (token is not null)
                {
                    request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
                }

                return request;
            }
            case SourceLinkProvider.GitLab when segments.Length >= 2:
            {
                var project = Uri.EscapeDataString(string.Join('/', segments));
                var request = new HttpRequestMessage(HttpMethod.Get, $"{repo.Scheme}://{repo.Authority}/api/v4/projects/{project}/repository/files/{Uri.EscapeDataString(path)}/raw?ref={Uri.EscapeDataString(gitRef)}");
                if (token is not null)
                {
                    request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token);
                }

                return request;
            }
            case SourceLinkProvider.AzureDevOps when segments.Length >= 4 && segments[^2] == "_git":
            {
                var project = string.Join('/', segments[..^2].Select(Uri.EscapeDataString));
                var query = $"path={Uri.EscapeDataString("/" + path)}&versionDescriptor.version={Uri.EscapeDataString(gitRef)}" +
                    $"&versionDescriptor.versionType={(isCommit ? "commit" : "branch")}&$format=text&api-version=7.1";
                var request = new HttpRequestMessage(HttpMethod.Get, $"{repo.Scheme}://{repo.Authority}/{project}/_apis/git/repositories/{Uri.EscapeDataString(segments[^1])}/items?{query}");
                if (token is not null)
                {
                    request.Headers.TryAddWithoutValidation("Authorization", $"Basic {Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(":" + token))}");
                }

                return request;
            }
            default:
                return null;
        }
    }
}
