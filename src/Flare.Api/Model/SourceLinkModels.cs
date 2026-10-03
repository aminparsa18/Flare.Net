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

        if (RepoUrl.Length > 500 || DefaultRef.Length > 200 || PathPrefix.Length > 500)
        {
            return "repoUrl, defaultRef, or pathPrefix is too long.";
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
