using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Flare.Api.Alerting;
using Flare.Api.Model;
using Flare.Identity.IssueTrackers;
using Flare.Identity.SourceLinks;

namespace Flare.Api.IssueTracking;

/// <summary>
/// Builds the host API request that creates one issue, and reads the new issue's URL out of the response - pure, so
/// the three hosts' URL/auth/body shapes are unit-tested without HTTP. See
/// docs-internal/adr/0275-create-issue-from-flare.md. The target host is always the stored config's, never the caller's.
/// </summary>
public static partial class IssueCreateRequestBuilder
{
    /// <summary>A GitHub or GitLab create request, or null when the config is not a repo of that provider or has no issue token.</summary>
    public static HttpRequestMessage? BuildRepoIssue(SourceLinkConfig config, string title, string body)
    {
        if (string.IsNullOrEmpty(config.IssueToken) || !Uri.TryCreate(config.RepoUrl, UriKind.Absolute, out var repo))
        {
            return null;
        }

        var segments = repo.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2)
        {
            return null;
        }

        switch (config.Provider)
        {
            case SourceLinkProvider.GitHub:
            {
                var api = repo.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ? "https://api.github.com" : $"{repo.Scheme}://{repo.Authority}/api/v3";
                var repoName = segments[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? segments[1][..^4] : segments[1];
                var request = new HttpRequestMessage(HttpMethod.Post, $"{api}/repos/{Uri.EscapeDataString(segments[0])}/{Uri.EscapeDataString(repoName)}/issues")
                {
                    Content = Json(new JsonObject { ["title"] = title, ["body"] = body })
                };
                request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
                request.Headers.TryAddWithoutValidation("User-Agent", "Flare");
                request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {config.IssueToken}");
                return request;
            }
            case SourceLinkProvider.GitLab:
            {
                var path = string.Join('/', segments);
                var project = Uri.EscapeDataString(path.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? path[..^4] : path);
                var request = new HttpRequestMessage(HttpMethod.Post, $"{repo.Scheme}://{repo.Authority}/api/v4/projects/{project}/issues")
                {
                    Content = Json(new JsonObject { ["title"] = title, ["description"] = body })
                };
                request.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", config.IssueToken);
                return request;
            }
            default:
                return null;
        }
    }

    /// <summary>
    /// A Jira create request, or null when the config has no API token or issue type. With an account email it is Jira Cloud
    /// (REST v3, Basic auth, Atlassian Document Format body); without one, Server/Data Center (REST v2, Bearer personal access
    /// token, wiki-text body). The project and issue type are the numeric ids the prefilled link also uses.
    /// </summary>
    public static HttpRequestMessage? BuildJiraIssue(IssueTrackerConfig config, string title, string body)
    {
        if (string.IsNullOrEmpty(config.ApiToken) || config.IssueTypeId.Length == 0 || !Uri.TryCreate(config.SiteUrl, UriKind.Absolute, out _))
        {
            return null;
        }

        var cloud = config.AccountEmail.Length > 0;
        var fields = new JsonObject
        {
            ["project"] = new JsonObject { ["id"] = config.ProjectId },
            ["issuetype"] = new JsonObject { ["id"] = config.IssueTypeId },
            ["summary"] = title,
            ["description"] = cloud ? JiraAlertNotifier.BuildAdf(body) : (JsonNode?)JsonValue.Create(body),
            ["labels"] = new JsonArray("flare"),
        };
        var request = new HttpRequestMessage(HttpMethod.Post, $"{config.SiteUrl.TrimEnd('/')}/rest/api/{(cloud ? 3 : 2)}/issue")
        {
            Content = Json(new JsonObject { ["fields"] = fields })
        };
        request.Headers.Authorization = cloud
            ? new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{config.AccountEmail}:{config.ApiToken}")))
            : new AuthenticationHeaderValue("Bearer", config.ApiToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    /// <summary>
    /// The new issue's browser URL from the host's response, or null when it has none or it is not an http(s) URL on
    /// <paramref name="configuredBase"/>'s host (a response must not be able to steer the stored link elsewhere).
    /// </summary>
    public static string? ParseIssueUrl(IssueTrackerTarget target, string responseBody, string configuredBase)
    {
        JsonNode? json;
        try
        {
            json = JsonNode.Parse(responseBody);
        }
        catch (JsonException)
        {
            return null;
        }

        if (!Uri.TryCreate(configuredBase, UriKind.Absolute, out var host))
        {
            return null;
        }

        string? url;
        if (target == IssueTrackerTarget.Jira)
        {
            var key = json?["key"]?.GetValue<string>();
            url = key is not null && JiraKey().IsMatch(key) ? $"{configuredBase.TrimEnd('/')}/browse/{key}" : null;
        }
        else
        {
            url = json?[target == IssueTrackerTarget.GitLab ? "web_url" : "html_url"]?.GetValue<string>();
            if (url is not null
                && (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("http" or "https") || !parsed.Authority.Equals(host.Authority, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }
        }

        return url is { Length: > 0 and <= 2048 } ? url : null;
    }

    private static StringContent Json(JsonObject body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9_]*-\d+$")]
    private static partial Regex JiraKey();
}
