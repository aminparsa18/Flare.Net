using Flare.Identity.SourceLinks;

namespace Flare.Api.Model;

/// <summary>
/// One service's source repository config for <c>/api/source-links</c> - see
/// docs-internal/adr/0095-exception-source-links.md. Plain JSON (camelCase, string enums),
/// not MemoryPack: a few small config rows, same call as <c>MetricAttributeRuleEndpoints</c>.
/// </summary>
public sealed record SourceLinkDto
{
    public required string ServiceName { get; init; }

    public required SourceLinkProvider Provider { get; init; }

    /// <summary>Repository root URL, e.g. <c>https://github.com/acme/shop</c> (Azure DevOps: <c>https://dev.azure.com/org/project/_git/repo</c>).</summary>
    public required string RepoUrl { get; init; }

    /// <summary>Branch/tag used when an exception's own commit can't be determined. Empty = no fallback.</summary>
    public string DefaultRef { get; init; } = "";

    /// <summary>Build-machine directory stripped from frame paths to get a repo-relative path. Empty = match by the frame's file name tail.</summary>
    public string PathPrefix { get; init; } = "";

    /// <summary>Write-only read-only-scope host token for inline source: null keeps the stored one, empty clears it. Never returned.</summary>
    public string? AccessToken { get; init; }

    /// <summary>Response-only: whether a token is stored.</summary>
    public bool HasAccessToken { get; init; }

    /// <summary>Write-only token with permission to open issues on the repo (GitHub / GitLab), for creating issues from Flare (ADR-0275). Separate from the read-only <see cref="AccessToken"/>: null keeps the stored one, empty clears it. Never returned.</summary>
    public string? IssueToken { get; init; }

    /// <summary>Response-only: whether an issue-creation token is stored.</summary>
    public bool HasIssueToken { get; init; }

    /// <summary>Returns a user-facing validation error, or null when valid.</summary>
    public string? Validate()
    {
        if (!Uri.TryCreate(RepoUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            return "repoUrl must be an absolute http(s) URL.";
        }

        if (!Enum.IsDefined(Provider))
        {
            return "provider is not recognised.";
        }

        // Omitted by an API client (curl, CLI, Terraform) they arrive as null despite the "" defaults.
        if (RepoUrl.Length > 500 || (DefaultRef?.Length ?? 0) > 200 || (PathPrefix?.Length ?? 0) > 500)
        {
            return "repoUrl, defaultRef, or pathPrefix is too long.";
        }

        if ((IssueToken?.Length ?? 0) > 500)
        {
            return "issueToken is too long.";
        }

        return null;
    }
}

public sealed record SourceLinkListResponse
{
    public required IReadOnlyList<SourceLinkDto> Links { get; init; }
}

/// <summary>Request for <c>POST /api/source-links/snippet</c>: lines around <see cref="Line"/> of <see cref="Path"/> (repo-relative) at <see cref="Ref"/>, from the service's configured repo.</summary>
public sealed record SourceSnippetRequest
{
    public required string ServiceName { get; init; }

    public required string Ref { get; init; }

    public bool IsCommit { get; init; }

    public required string Path { get; init; }

    public required int Line { get; init; }
}

public sealed record SourceSnippetResponse
{
    /// <summary>1-based number of <see cref="Lines"/>[0].</summary>
    public required int StartLine { get; init; }

    public required IReadOnlyList<string> Lines { get; init; }
}

/// <summary>
/// Request for <c>POST /api/source-links/suspects</c> (ADR-0263): which commits between two refs of the service's repo
/// changed the files in a failing stack. <see cref="Paths"/> are repo-relative, throw site first.
/// </summary>
public sealed record SuspectCommitsRequest
{
    public const int MaxPaths = 30;

    public required string ServiceName { get; init; }

    /// <summary>The commit (or ref) of the release before the one that introduced the error.</summary>
    public required string BaseRef { get; init; }

    /// <summary>The commit (or ref) of the release that introduced it.</summary>
    public required string HeadRef { get; init; }

    public required IReadOnlyList<string> Paths { get; init; }
}

public sealed record SuspectCommit
{
    public required string Sha { get; init; }

    /// <summary>First line of the commit message.</summary>
    public required string Message { get; init; }

    public required string Author { get; init; }

    public DateTimeOffset? Date { get; init; }

    public required string Url { get; init; }

    /// <summary>Which of the request's paths this commit changed, throw site first.</summary>
    public required IReadOnlyList<string> MatchedPaths { get; init; }
}

public sealed record SuspectCommitsResponse
{
    /// <summary>Best candidate first; empty when no commit in the range touched a path.</summary>
    public required IReadOnlyList<SuspectCommit> Commits { get; init; }

    /// <summary>Commits in the range.</summary>
    public required int TotalCommits { get; init; }

    /// <summary>True when the range was longer than what was inspected, so a suspect may be missing.</summary>
    public bool Truncated { get; init; }

    /// <summary>The host's compare page for the range.</summary>
    public required string CompareUrl { get; init; }
}
