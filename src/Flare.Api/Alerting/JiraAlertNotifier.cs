using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Alerting;

/// <summary>
/// Opens, updates and closes a Jira Cloud issue per firing rule, for a
/// <see cref="NotificationChannelType.Jira"/> channel (REST API v3, HTTP Basic with the
/// channel's email + API token). See <c>docs-internal/adr/0097-jira-notification-channel.md</c>.
/// </summary>
/// <remarks>
/// Stateless: an issue is tied to its rule by the label <see cref="RuleLabel"/> and found with
/// JQL (<c>statusCategory != Done</c>), so no issue key is stored anywhere. A fire with an open
/// issue adds a comment; with none, creates the issue. A recovery comments and transitions the
/// issue to the first transition whose target status is in the "done" category. A test send
/// always creates a fresh issue under a one-off label so it never touches a real one.
/// </remarks>
public sealed class JiraAlertNotifier(HttpClient httpClient, IOptions<AlertLinkOptions> linkOptions) : IAlertNotifier
{
    internal const int MaxSummaryLength = 255;

    /// <summary>The label that ties a rule's issues together - see this class's remarks.</summary>
    public static string RuleLabel(AlertRule rule) => $"flare-alert-{rule.Id:N}";

    internal static string BuildSummary(AlertMessage message)
    {
        var line = (message.Title ?? message.Text).Split('\n', 2)[0].Trim();
        return line.Length <= MaxSummaryLength ? line : line[..(MaxSummaryLength - 1)] + "…";
    }

    /// <summary>Atlassian Document Format: one paragraph per non-empty line (the only structure the built-in text has).</summary>
    internal static JsonObject BuildDescription(AlertMessage message)
    {
        var content = new JsonArray();
        foreach (var line in message.Text.Split('\n'))
        {
            if (line.Length == 0)
            {
                continue;
            }

            content.Add(new JsonObject
            {
                ["type"] = "paragraph",
                ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = line }),
            });
        }

        if (content.Count == 0)
        {
            content.Add(new JsonObject { ["type"] = "paragraph", ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = "(no details)" }) });
        }

        return new JsonObject { ["type"] = "doc", ["version"] = 1, ["content"] = content };
    }

    internal static string BuildOpenIssueJql(string projectKey, string label) =>
        $"project = \"{projectKey.Replace("\"", "")}\" AND labels = \"{label}\" AND statusCategory != Done ORDER BY created DESC";

    public async Task<NotificationResult> SendAsync(AlertRule rule, NotificationChannel channel, double observedValue, DateTimeOffset firedAt, CancellationToken cancellationToken, bool isTest = false, string? metricUnit = null, bool noData = false, AnomalyScore? anomaly = null, bool resolved = false, string? logSamples = null)
    {
        try
        {
            var message = AlertMessageFormatter.BuildMessage(rule, observedValue, isTest, linkOptions.Value.PublicUrl, metricUnit, firedAt, noData, anomaly, resolved: resolved, logSamples: logSamples);
            var label = isTest ? $"flare-test-{Guid.NewGuid():N}" : RuleLabel(rule);

            string? openKey = null;
            if (!isTest)
            {
                var (found, searchFailure) = await FindOpenIssueAsync(channel, label, cancellationToken);
                if (searchFailure is not null)
                {
                    return searchFailure;
                }

                openKey = found;
            }

            if (resolved)
            {
                if (openKey is null)
                {
                    // Nothing open (already closed by hand, or the fire never created one).
                    return new NotificationResult(true, 200, null);
                }

                var comment = await PostAsync(channel, $"/rest/api/3/issue/{openKey}/comment", new JsonObject { ["body"] = BuildDescription(message) }, cancellationToken);
                if (!comment.Success)
                {
                    return comment;
                }

                return await TransitionToDoneAsync(channel, openKey, cancellationToken);
            }

            if (openKey is not null)
            {
                return await PostAsync(channel, $"/rest/api/3/issue/{openKey}/comment", new JsonObject { ["body"] = BuildDescription(message) }, cancellationToken);
            }

            var issue = new JsonObject
            {
                ["fields"] = new JsonObject
                {
                    ["project"] = new JsonObject { ["key"] = channel.JiraProjectKey },
                    ["issuetype"] = new JsonObject { ["name"] = string.IsNullOrWhiteSpace(channel.JiraIssueType) ? "Task" : channel.JiraIssueType },
                    ["summary"] = BuildSummary(message),
                    ["description"] = BuildDescription(message),
                    ["labels"] = new JsonArray("flare", label),
                },
            };
            return await PostAsync(channel, "/rest/api/3/issue", issue, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or UriFormatException or JsonException)
        {
            return new NotificationResult(false, 0, ex.Message);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // Client-side timeout, not app shutdown - see WebhookAlertNotifier.
            return new NotificationResult(false, 0, ex.Message);
        }
    }

    private async Task<(string? Key, NotificationResult? Failure)> FindOpenIssueAsync(NotificationChannel channel, string label, CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["jql"] = BuildOpenIssueJql(channel.JiraProjectKey, label),
            ["maxResults"] = 1,
            ["fields"] = new JsonArray("summary"),
        };
        using var response = await SendRequestAsync(channel, "/rest/api/3/search/jql", body, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return (null, await FailureAsync(response, cancellationToken));
        }

        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return (json?["issues"]?.AsArray().FirstOrDefault()?["key"]?.GetValue<string>(), null);
    }

    private async Task<NotificationResult> TransitionToDoneAsync(NotificationChannel channel, string issueKey, CancellationToken cancellationToken)
    {
        using var listRequest = CreateRequest(channel, HttpMethod.Get, $"/rest/api/3/issue/{issueKey}/transitions");
        using var listResponse = await httpClient.SendAsync(listRequest, cancellationToken);
        if (!listResponse.IsSuccessStatusCode)
        {
            return await FailureAsync(listResponse, cancellationToken);
        }

        var json = JsonNode.Parse(await listResponse.Content.ReadAsStringAsync(cancellationToken));
        var transitionId = json?["transitions"]?.AsArray()
            .FirstOrDefault(t => t?["to"]?["statusCategory"]?["key"]?.GetValue<string>() == "done")?["id"]?.GetValue<string>();
        if (transitionId is null)
        {
            return new NotificationResult(false, 0, $"No transition to a Done status is available on {issueKey}.");
        }

        return await PostAsync(channel, $"/rest/api/3/issue/{issueKey}/transitions", new JsonObject { ["transition"] = new JsonObject { ["id"] = transitionId } }, cancellationToken);
    }

    private async Task<NotificationResult> PostAsync(NotificationChannel channel, string path, JsonObject body, CancellationToken cancellationToken)
    {
        using var response = await SendRequestAsync(channel, path, body, cancellationToken);
        return response.IsSuccessStatusCode
            ? new NotificationResult(true, (int)response.StatusCode, null)
            : await FailureAsync(response, cancellationToken);
    }

    private Task<HttpResponseMessage> SendRequestAsync(NotificationChannel channel, string path, JsonObject body, CancellationToken cancellationToken)
    {
        var request = CreateRequest(channel, HttpMethod.Post, path);
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        return httpClient.SendAsync(request, cancellationToken);
    }

    private static HttpRequestMessage CreateRequest(NotificationChannel channel, HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, channel.JiraBaseUrl.TrimEnd('/') + path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{channel.JiraEmail}:{channel.JiraApiToken}")));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    /// <summary>Jira answers a rejection with <c>{"errorMessages":[...],"errors":{field:msg}}</c>; surface it, capped.</summary>
    private static async Task<NotificationResult> FailureAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string? detail = null;
        try
        {
            var raw = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            var json = JsonNode.Parse(raw);
            var parts = new List<string>();
            if (json?["errorMessages"] is JsonArray messages)
            {
                parts.AddRange(messages.Select(m => m?.ToString() ?? "").Where(m => m.Length > 0));
            }

            if (json?["errors"] is JsonObject errors)
            {
                parts.AddRange(errors.Select(e => $"{e.Key}: {e.Value}"));
            }

            detail = parts.Count > 0 ? string.Join("; ", parts) : raw;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException)
        {
            // Body unreadable or not JSON - fall back to the bare status below.
        }

        if (detail is { Length: > 300 })
        {
            detail = detail[..300] + "…";
        }

        return new NotificationResult(false, (int)response.StatusCode, string.IsNullOrEmpty(detail) ? $"HTTP {(int)response.StatusCode}" : $"HTTP {(int)response.StatusCode}: {detail}");
    }
}
