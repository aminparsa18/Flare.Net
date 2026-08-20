namespace Flare.Api.DockerResources;

/// <summary>
/// Thin client over a <c>tecnativa/docker-socket-proxy</c> (or any Docker-Engine-API-
/// compatible HTTP endpoint) - see <see cref="DockerResourcesOptions.ProxyUrl"/>. Talks
/// plain HTTP, never a Unix socket: the proxy is always the thing actually holding
/// <c>/var/run/docker.sock</c>, so Flare.Api only ever needs an ordinary
/// <see cref="HttpClient"/> pointed at the proxy's TCP listener - no
/// <c>SocketsHttpHandler.ConnectCallback</c>, no <c>Docker.DotNet</c> dependency. No API
/// version prefix on any path either - Docker's HTTP API serves its latest mutually-
/// compatible version when none is given, so there's nothing to keep pinned/in sync as the
/// daemon or proxy image gets upgraded.
/// </summary>
public sealed class DockerEngineClient(HttpClient httpClient)
{
    /// <summary>
    /// Lists every container - running or not (<c>all=1</c>, so a stopped Flare container
    /// still shows up as <see cref="Model.ResourceState.Exited"/> instead of vanishing from
    /// the graph) - carrying the <c>flare.resource=true</c> identity label. See
    /// <c>docs/prompts/docker-resources-graph-prompt.md</c> for why this label exists
    /// rather than relying on <c>com.docker.compose.project</c> (doesn't apply to
    /// Aspire/DCP-created containers) to scope "containers that are part of my own install"
    /// out of everything else running on the Docker host.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListFlareContainerIdsAsync(CancellationToken cancellationToken)
    {
        const string filters = /*lang=json,strict*/ """{"label":["flare.resource=true"]}""";
        var url = $"/containers/json?all=1&filters={Uri.EscapeDataString(filters)}";

        var summaries = await httpClient.GetFromJsonAsync(url, DockerApiJsonContext.Default.DockerContainerSummaryArray, cancellationToken)
            ?? [];
        return summaries.Select(s => s.Id).Where(id => !string.IsNullOrEmpty(id)).ToArray();
    }

    /// <summary>
    /// Full inspect for one container - the only call that returns structured
    /// <c>.State.Health</c>; the list endpoint only ever folds health into its
    /// human-readable <c>Status</c> string (e.g. <c>"Up 2 minutes (healthy)"</c>). Internal
    /// (not public) purely because its return type, <see cref="DockerContainerInspect"/>,
    /// is itself internal - this client is a <c>DockerResources</c>-internal implementation
    /// detail, not part of this project's public wire surface.
    /// </summary>
    internal async Task<DockerContainerInspect?> InspectAsync(string containerId, CancellationToken cancellationToken) =>
        await httpClient.GetFromJsonAsync($"/containers/{containerId}/json", DockerApiJsonContext.Default.DockerContainerInspect, cancellationToken);
}
