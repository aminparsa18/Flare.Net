using Flare.Api.Model;

namespace Flare.Api.ResourceGraph;

/// <summary>
/// Parses a <c>flare.relationships</c> label value (e.g.
/// <c>"clickhouse:Reference,redis:Reference"</c>) into <see cref="ResourceEdgeDto"/>s sourced
/// from the owning role - shared by both topology providers since the label vocabulary and
/// meaning is identical on both (a Docker container label under
/// <c>DockerResources.DockerContainerPoller</c>, a Kubernetes pod-template label under
/// <c>KubernetesResources.KubernetesResourcePoller</c> - see
/// <c>Aspire.Hosting.Flare</c>'s <c>WithFlareResourceLabels</c>, which stamps the exact same
/// label onto both).
/// </summary>
public static class RelationshipLabelParser
{
    /// <summary>Malformed entries are skipped individually, not fatal to the rest of the label.</summary>
    public static IEnumerable<ResourceEdgeDto> Parse(string sourceRole, IDictionary<string, string> labels)
    {
        if (!labels.TryGetValue("flare.relationships", out var relationships) || string.IsNullOrWhiteSpace(relationships))
        {
            yield break;
        }

        foreach (var entry in relationships.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = entry.Split(':', 2);
            if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            {
                continue;
            }

            yield return new ResourceEdgeDto { SourceRole = sourceRole, TargetRole = parts[0], RelationshipType = parts[1] };
        }
    }
}
