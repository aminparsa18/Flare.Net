namespace Flare.Api.KubernetesResources;

/// <summary>
/// Tuning knobs for the Kubernetes-driven Resources page (<c>GET /api/resources/snapshot</c> /
/// <c>GET /api/resources/watch</c> - shared with the Docker provider via
/// <c>ResourceGraph.ResourceGraphSourceRegistry</c>). Bound from the
/// <c>KubernetesResources</c> configuration section.
/// </summary>
/// <remarks>
/// See <see cref="KubernetesResourcePoller"/> - the background reader that polls the
/// in-cluster Kubernetes API server for Flare-labeled Pods/Services and publishes the
/// computed graph into the shared registry. <see cref="Enabled"/> is this feature's entire
/// "off" switch, mirroring <c>DockerResources.DockerResourcesOptions.ProxyUrl</c>'s "absent
/// config = off" pattern - a plain bool rather than a URL here since there's no equivalent
/// "which endpoint" config to double as the switch (the in-cluster API server's address is
/// always <c>KUBERNETES_SERVICE_HOST</c>/<c>KUBERNETES_SERVICE_PORT</c>, never something a
/// consumer configures).
/// </remarks>
public sealed class KubernetesResourcesOptions
{
    public const string SectionName = "KubernetesResources";

    /// <summary>
    /// <see langword="false"/> (the default) disables this feature entirely - set by
    /// <c>Aspire.Hosting.Flare</c>'s <c>AddFlare(enableResourceGraph: true)</c> only when a
    /// Kubernetes deployment target is registered (see
    /// <c>FlareResourceBuilderExtensions.AddFlare</c>'s <c>enableResourceGraph</c> doc
    /// comment). Flare.Api never attempts <c>KubernetesClientConfiguration.InClusterConfig()</c>
    /// when this is <see langword="false"/> - that call throws outside a real cluster, so it
    /// must stay gated behind this flag rather than attempted unconditionally at startup.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>How often to re-list Flare-labeled Pods/Services. Same interval and reasoning as <c>DockerResources.DockerResourcesOptions.PollDelay</c>.</summary>
    public TimeSpan PollDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How far back to look for "recently active" producer services - same meaning and default as <c>DockerResources.DockerResourcesOptions.ProducerActivityWindow</c>.</summary>
    public TimeSpan ProducerActivityWindow { get; set; } = TimeSpan.FromMinutes(5);
}
