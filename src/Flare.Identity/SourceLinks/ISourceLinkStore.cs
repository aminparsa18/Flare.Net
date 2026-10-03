namespace Flare.Identity.SourceLinks;

/// <summary>Which URL pattern a repo host uses for "file at commit, line N" links.</summary>
public enum SourceLinkProvider
{
    GitHub,
    GitLab,
    AzureDevOps
}

/// <summary>
/// One service's source repository config. <see cref="DefaultRef"/> is the branch/tag used
/// when an exception's own commit can't be determined; <see cref="PathPrefix"/> is the
/// build-machine directory stripped from stack-frame paths to get a repo-relative path
/// (e.g. <c>/_/</c> for deterministic builds, <c>/src/</c> inside a Docker build).
/// </summary>
public sealed record SourceLinkConfig(
    string ServiceName,
    SourceLinkProvider Provider,
    string RepoUrl,
    string DefaultRef,
    string PathPrefix);

/// <summary>Per-service source-repo config for exception stack-trace links - see docs-internal/adr/0095-exception-source-links.md.</summary>
public interface ISourceLinkStore
{
    Task<IReadOnlyList<SourceLinkConfig>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Upserts one service's config.</summary>
    Task SetAsync(SourceLinkConfig config, CancellationToken cancellationToken = default);

    /// <summary>Removes a service's config. A no-op if none exists.</summary>
    Task DeleteAsync(string serviceName, CancellationToken cancellationToken = default);
}
