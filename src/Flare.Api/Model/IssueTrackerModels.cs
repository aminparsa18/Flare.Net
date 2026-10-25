using Flare.Identity.IssueTrackers;

namespace Flare.Api.Model;

/// <summary>
/// One service's issue tracker config for <c>/api/issue-trackers</c> - see
/// docs-internal/adr/0274-jira-new-issue-link.md. Plain JSON (camelCase, string enums), same call
/// as <see cref="SourceLinkDto"/>. Holds no secret: the dashboard builds the prefilled create URL.
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

        return null;
    }

    private static bool IsDigits(string value) => value.Length is > 0 and <= 20 && value.All(char.IsAsciiDigit);
}

public sealed record IssueTrackerListResponse
{
    public required IReadOnlyList<IssueTrackerDto> Trackers { get; init; }
}
