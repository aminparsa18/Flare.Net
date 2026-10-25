using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Flare.Api.Errors;
using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Api.Source;
using Flare.Identity.IssueTrackers;
using Flare.Identity.SourceLinks;

namespace Flare.Api.IssueTracking;

/// <summary>What <see cref="IIssueCreationService.CreateAsync"/> did: the group's updated state, or an HTTP status and message.</summary>
public sealed record IssueCreateOutcome(ErrorIssue? Issue, int StatusCode, string? Error)
{
    public static IssueCreateOutcome Fail(int statusCode, string error) => new(null, statusCode, error);
}

public interface IIssueCreationService
{
    /// <summary>Creates the issue in the configured tracker and links it to the group. Never throws for host/validation failures.</summary>
    Task<IssueCreateOutcome> CreateAsync(CreateTrackerIssueRequest request, string changedBy, CancellationToken cancellationToken);
}

/// <summary>
/// Creates an issue for an /errors group in GitHub, GitLab or Jira and stores its URL on the group - see
/// docs-internal/adr/0275-create-issue-from-flare.md. Bounded like <see cref="SourceSnippetService"/>: the host comes only
/// from admin-set config, no redirects (the token must not follow one off-host), 10s timeout, small response cap; and
/// never retried, since a retried create after a timeout could open a second issue.
/// </summary>
public sealed class IssueCreationService(
    IHttpClientFactory httpClientFactory,
    ISourceLinkStore sourceLinks,
    IIssueTrackerStore trackers,
    IErrorIssueQueryService issues,
    ILogger<IssueCreationService> logger) : IIssueCreationService
{
    public const string HttpClientName = "issue-create";
    private const int MaxBodyBytes = 256 * 1024;

    // Groups with a create in flight. A second click on the same group fails fast instead of racing to open a second issue.
    // In-process only: two API replicas could still both create, which the ADR notes.
    private readonly ConcurrentDictionary<string, byte> _inFlight = new();

    public async Task<IssueCreateOutcome> CreateAsync(CreateTrackerIssueRequest request, string changedBy, CancellationToken cancellationToken)
    {
        var groupId = ErrorIssueFingerprint.Compute(request.ExceptionType, request.ExceptionMessage);
        if (!_inFlight.TryAdd(groupId, 0))
        {
            return IssueCreateOutcome.Fail(StatusCodes.Status409Conflict, "An issue is already being created for this group.");
        }

        try
        {
            var existing = await issues.GetIssueUrlAsync(request.ExceptionType, request.ExceptionMessage, cancellationToken);
            if (existing.Length > 0)
            {
                return IssueCreateOutcome.Fail(StatusCodes.Status409Conflict, $"This group is already linked to an issue: {existing}");
            }

            var (message, baseUrl, configError) = await BuildAsync(request, cancellationToken);
            if (message is null)
            {
                return IssueCreateOutcome.Fail(StatusCodes.Status422UnprocessableEntity, configError!);
            }

            using (message)
            {
                var (issueUrl, sendError) = await SendAsync(message, request.Target, baseUrl!, cancellationToken);
                if (issueUrl is null)
                {
                    return IssueCreateOutcome.Fail(StatusCodes.Status502BadGateway, sendError!);
                }

                try
                {
                    var linked = await issues.UpsertAsync(
                        new ErrorIssueRequest { ExceptionType = request.ExceptionType, ExceptionMessage = request.ExceptionMessage, IssueUrl = issueUrl },
                        changedBy,
                        cancellationToken);
                    return new IssueCreateOutcome(linked, StatusCodes.Status200OK, null);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // The issue exists on the host; say where, so it can be linked by hand.
                    logger.LogError(ex, "Created {Target} issue {IssueUrl} but could not link it to group {GroupId}", request.Target, issueUrl, groupId);
                    return IssueCreateOutcome.Fail(StatusCodes.Status500InternalServerError, $"The issue was created at {issueUrl} but could not be linked to the group; link it manually.");
                }
            }
        }
        finally
        {
            _inFlight.TryRemove(groupId, out _);
        }
    }

    private async Task<(HttpRequestMessage? Message, string? BaseUrl, string? Error)> BuildAsync(CreateTrackerIssueRequest request, CancellationToken cancellationToken)
    {
        if (request.Target == IssueTrackerTarget.Jira)
        {
            var tracker = (await trackers.GetAllAsync(cancellationToken)).FirstOrDefault(t => t.ServiceName == request.ServiceName);
            if (tracker is null)
            {
                return (null, null, "No Jira issue tracker is configured for this service.");
            }

            if (string.IsNullOrEmpty(tracker.ApiToken))
            {
                return (null, null, "The Jira issue tracker has no API token. Add one to create issues from Flare.");
            }

            if (tracker.IssueTypeId.Length == 0)
            {
                return (null, null, "Set a numeric issue type id on the Jira issue tracker to create issues from Flare.");
            }

            return (IssueCreateRequestBuilder.BuildJiraIssue(tracker, request.Title, request.Body), tracker.SiteUrl, "The Jira issue tracker config is invalid.");
        }

        var wanted = request.Target == IssueTrackerTarget.GitLab ? SourceLinkProvider.GitLab : SourceLinkProvider.GitHub;
        var link = (await sourceLinks.GetAllAsync(cancellationToken)).FirstOrDefault(c => c.ServiceName == request.ServiceName && c.Provider == wanted);
        if (link is null)
        {
            return (null, null, $"No {wanted} source repository is configured for this service.");
        }

        if (string.IsNullOrEmpty(link.IssueToken))
        {
            return (null, null, "The source repository has no issue-creation token. Add one to create issues from Flare.");
        }

        return (IssueCreateRequestBuilder.BuildRepoIssue(link, request.Title, request.Body), link.RepoUrl, "The configured repository URL is not valid for this host.");
    }

    private async Task<(string? Url, string? Error)> SendAsync(HttpRequestMessage message, IssueTrackerTarget target, string baseUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var noRetry = Flare.ServiceDefaults.HttpRetryScope.SingleShot();
            using var response = await httpClientFactory.CreateClient(HttpClientName).SendAsync(message, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            var text = await SourceSnippetService.ReadCappedAsync(response, MaxBodyBytes, timeout.Token);
            if (text is null)
            {
                return (null, "The tracker returned an unexpectedly large response.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var hint = (int)response.StatusCode is 401 or 403 or 404 ? " Check the token's permissions and the configured project." : "";
                return (null, $"The tracker returned {(int)response.StatusCode}{Describe(text)}.{hint}");
            }

            return IssueCreateRequestBuilder.ParseIssueUrl(target, text, baseUrl) is { } url
                ? (url, null)
                : (null, "The tracker created an issue but its response did not include a usable URL.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            return (null, "Could not reach the tracker.");
        }
    }

    /// <summary>": &lt;message&gt;" from a host's JSON error body (GitHub/GitLab <c>message</c>, Jira <c>errorMessages</c> / <c>errors</c>), or "".</summary>
    internal static string Describe(string body)
    {
        try
        {
            var json = JsonNode.Parse(body);
            var text = json?["message"]?.ToString()
                ?? json?["error"]?.ToString()
                ?? (json?["errorMessages"] as JsonArray)?.FirstOrDefault()?.ToString()
                ?? (json?["errors"] as JsonObject)?.FirstOrDefault().Value?.ToString();
            return string.IsNullOrWhiteSpace(text) ? "" : ": " + (text.Length > 200 ? text[..200] : text);
        }
        catch (System.Text.Json.JsonException)
        {
            return "";
        }
    }
}
