namespace Flare.Api.Model;

/// <summary>Which tracker an issue is created in (ADR-0275). GitHub and GitLab use the service's source-link repo; Jira its issue tracker config.</summary>
public enum IssueTrackerTarget
{
    GitHub,
    GitLab,
    Jira
}

/// <summary>
/// Request body for <c>POST /api/errors/issues/create</c>: create an issue for one exception group in the
/// tracker configured for <see cref="ServiceName"/> and link it to the group. The title and body come from the
/// dashboard (the same text its prefilled links carry); the host and credentials never do.
/// </summary>
public sealed record CreateTrackerIssueRequest
{
    public const int MaxTitleLength = 256;
    public const int MaxBodyLength = 8000;

    public required IssueTrackerTarget Target { get; init; }

    /// <summary>The service whose source-link repo (GitHub, GitLab) or issue tracker (Jira) receives the issue.</summary>
    public required string ServiceName { get; init; }

    public required string ExceptionType { get; init; }

    public string ExceptionMessage { get; init; } = "";

    public required string Title { get; init; }

    /// <summary>Markdown for GitHub and GitLab, plain/wiki text for Jira.</summary>
    public string Body { get; init; } = "";

    /// <summary>Returns a user-facing validation error, or null when valid.</summary>
    public string? Validate()
    {
        if (!Enum.IsDefined(Target))
        {
            return "target is not recognised.";
        }

        if (string.IsNullOrWhiteSpace(ServiceName) || string.IsNullOrWhiteSpace(ExceptionType))
        {
            return "serviceName and exceptionType are required.";
        }

        if (string.IsNullOrWhiteSpace(Title) || Title.Length > MaxTitleLength)
        {
            return $"title is required and at most {MaxTitleLength} characters.";
        }

        // Omitted by an API client it arrives as null despite the default.
        if ((Body?.Length ?? 0) > MaxBodyLength)
        {
            return $"body must be at most {MaxBodyLength} characters.";
        }

        return null;
    }
}
