namespace Flare.Identity.IssueTrackers;

/// <summary>Which tracker a service's "new issue" link opens.</summary>
public enum IssueTrackerProvider
{
    Jira
}

/// <summary>
/// One service's issue tracker config. For Jira, <see cref="SiteUrl"/> is the site root
/// (<c>https://acme.atlassian.net</c>, or a Server/Data Center base URL including any context path),
/// <see cref="ProjectId"/> the numeric project id (not the key - the prefilled create URL takes the id) and
/// <see cref="IssueTypeId"/> an optional numeric issue type id; empty leaves the type to Jira's default.
/// </summary>
public sealed record IssueTrackerConfig(
    string ServiceName,
    IssueTrackerProvider Provider,
    string SiteUrl,
    string ProjectId,
    string IssueTypeId);

/// <summary>Per-service issue tracker config - see docs-internal/adr/0274-jira-new-issue-link.md.</summary>
public interface IIssueTrackerStore
{
    Task<IReadOnlyList<IssueTrackerConfig>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Upserts one service's config.</summary>
    Task SetAsync(IssueTrackerConfig config, CancellationToken cancellationToken = default);

    /// <summary>Removes a service's config. A no-op if none exists.</summary>
    Task DeleteAsync(string serviceName, CancellationToken cancellationToken = default);
}
