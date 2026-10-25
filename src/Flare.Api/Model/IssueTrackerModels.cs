using Flare.Identity.IssueTrackers;

namespace Flare.Api.Model;

/// <summary>
/// One service's issue tracker config for <c>/api/issue-trackers</c> - see
/// docs-internal/adr/0274-jira-new-issue-link.md. Plain JSON (camelCase, string enums), same call
/// as <see cref="SourceLinkDto"/>. The API token is write-only (never returned, <see cref="HasApiToken"/> says whether one is stored); the dashboard still builds the prefilled create URL itself.
/// </summary>
public sealed record IssueTrackerDto
{
    public required string ServiceName { get; init; }

    public required IssueTrackerProvider Provider { get; init; }

    /// <summary>Jira site root, e.g. <c>https://acme.atlassian.net</c>.</summary>
    public required string SiteUrl { get; init; }

    /// <summary>Numeric Jira project id (digits only).</summary>
    public required string ProjectId { get; init; }

    /// <summary>Optional numeric issue type id. Empty = Jira's default type.</summary>
    public string IssueTypeId { get; init; } = "";

    /// <summary>Jira Cloud account email for creating issues from Flare (ADR-0275). Empty = a Server/Data Center personal access token (Bearer) instead of Basic auth.</summary>
    public string AccountEmail { get; init; } = "";

    /// <summary>Write-only Jira API token for creating issues from Flare: null keeps the stored one, empty clears it. Never returned.</summary>
    public string? ApiToken { get; init; }

    /// <summary>Response-only: whether an API token is stored.</summary>
    public bool HasApiToken { get; init; }

    /// <summary>Returns a user-facing validation error, or null when valid.</summary>
    public string? Validate()
    {
        if (!Uri.TryCreate(SiteUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            return "siteUrl must be an absolute http(s) URL.";
        }

        if (!Enum.IsDefined(Provider))
        {
            return "provider is not recognised.";
        }

        if (!IsDigits(ProjectId))
        {
            return "projectId must be the numeric Jira project id.";
        }

        if (IssueTypeId.Length > 0 && !IsDigits(IssueTypeId))
        {
            return "issueTypeId must be a numeric Jira issue type id.";
        }

        if (SiteUrl.Length > 500)
        {
            return "siteUrl is too long.";
        }

        // Omitted by an API client (Terraform, curl) it can arrive as null despite the default.
        if (AccountEmail is { Length: > 0 } && (AccountEmail.Length > 200 || !AccountEmail.Contains('@') || AccountEmail.Any(char.IsWhiteSpace)))
        {
            return "accountEmail must be an email address.";
        }

        if ((ApiToken?.Length ?? 0) > 2000)
        {
            return "apiToken is too long.";
        }

        return null;
    }

    private static bool IsDigits(string value) => value.Length is > 0 and <= 20 && value.All(char.IsAsciiDigit);
}

public sealed record IssueTrackerListResponse
{
    public required IReadOnlyList<IssueTrackerDto> Trackers { get; init; }
}
