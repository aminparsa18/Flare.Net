using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Xunit;

namespace Flare.Api.Tests.DockerResources;

/// <summary>Round-trip + wire-shape tests for <see cref="ResourceGraphJsonContext"/> - camelCase properties, string enums, matching every other outbound <c>*JsonContext</c> in this project.</summary>
public class ResourceGraphJsonContextTests
{
    [Fact]
    public void RoundTrips_Snapshot_WithNodesAndEdgesPopulated()
    {
        var original = new ResourceGraphSnapshot
        {
            Available = true,
            UnavailableReason = null,
            Provider = "Docker",
            UpdatedAt = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero),
            Nodes =
            [
                new ResourceNodeDto
                {
                    Id = "abc123def456",
                    Role = "ingest",
                    Name = "flare-net-ingest-1",
                    Image = "xracer007/flare-ingest:edge",
                    State = ResourceState.Running,
                    Health = ResourceHealth.Healthy,
                    Urls = ["http://localhost:4318"],
                    Kind = "Container",
                },
            ],
            Edges =
            [
                new ResourceEdgeDto { SourceRole = "ingest", TargetRole = "clickhouse", RelationshipType = "Reference" },
            ],
        };

        var json = JsonSerializer.Serialize(original, ResourceGraphJsonContext.Default.ResourceGraphSnapshot);
        var roundTripped = JsonSerializer.Deserialize(json, ResourceGraphJsonContext.Default.ResourceGraphSnapshot);

        Assert.NotNull(roundTripped);
        Assert.Equal(original.Available, roundTripped.Available);
        Assert.Equal(original.UnavailableReason, roundTripped.UnavailableReason);
        Assert.Equal(original.UpdatedAt, roundTripped.UpdatedAt);
        Assert.Equal(original.Nodes.Count, roundTripped.Nodes.Count);
        Assert.Equal(original.Nodes[0].Id, roundTripped.Nodes[0].Id);
        Assert.Equal(original.Nodes[0].Role, roundTripped.Nodes[0].Role);
        Assert.Equal(original.Nodes[0].Name, roundTripped.Nodes[0].Name);
        Assert.Equal(original.Nodes[0].Image, roundTripped.Nodes[0].Image);
        Assert.Equal(original.Nodes[0].State, roundTripped.Nodes[0].State);
        Assert.Equal(original.Nodes[0].Health, roundTripped.Nodes[0].Health);
        Assert.Equal(original.Nodes[0].Urls, roundTripped.Nodes[0].Urls);
        Assert.Equal(original.Nodes[0].Kind, roundTripped.Nodes[0].Kind);
        Assert.Equal(original.Nodes[0].ParentId, roundTripped.Nodes[0].ParentId);
        Assert.Equal(original.Provider, roundTripped.Provider);
        Assert.Equal(original.Edges.Count, roundTripped.Edges.Count);
        Assert.Equal(original.Edges[0].SourceRole, roundTripped.Edges[0].SourceRole);
        Assert.Equal(original.Edges[0].TargetRole, roundTripped.Edges[0].TargetRole);
        Assert.Equal(original.Edges[0].RelationshipType, roundTripped.Edges[0].RelationshipType);
    }

    [Fact]
    public void Serializes_NotEnabledSnapshot_WithCamelCasePropertiesAndPascalCaseEnumValues()
    {
        var snapshot = new ResourceGraphSnapshot
        {
            Available = false,
            UnavailableReason = "The Docker resource graph isn't enabled.",
        };

        var json = JsonSerializer.Serialize(snapshot, ResourceGraphJsonContext.Default.ResourceGraphSnapshot);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // camelCase properties (matches LogsJsonContext/LogTailJsonContext) ...
        Assert.False(root.GetProperty("available").GetBoolean());
        Assert.Equal("The Docker resource graph isn't enabled.", root.GetProperty("unavailableReason").GetString());
        Assert.Equal(JsonValueKind.Array, root.GetProperty("nodes").ValueKind);
        Assert.Empty(root.GetProperty("nodes").EnumerateArray());
    }

    [Fact]
    public void Serializes_StateAndHealthEnums_AsPascalCaseStringValues_NotCamelCased()
    {
        // UseStringEnumConverter (no naming policy applied to enum *values*, only to
        // property names) means enum values stay PascalCase even though property names are
        // camelCase - same documented asymmetry as LogsJsonContext/LogTailJsonContext (see
        // src/dashboard/src/lib/api.ts's top-of-file casing note).
        var snapshot = new ResourceGraphSnapshot
        {
            Available = true,
            Nodes =
            [
                new ResourceNodeDto
                {
                    Id = "abc123def456",
                    Role = "api",
                    Name = "flare-net-api-1",
                    Image = "xracer007/flare-api:edge",
                    State = ResourceState.Running,
                    Health = ResourceHealth.Unhealthy,
                    Kind = "Container",
                },
            ],
        };

        var json = JsonSerializer.Serialize(snapshot, ResourceGraphJsonContext.Default.ResourceGraphSnapshot);
        using var doc = JsonDocument.Parse(json);
        var node = doc.RootElement.GetProperty("nodes")[0];

        Assert.Equal("Running", node.GetProperty("state").GetString());
        Assert.Equal("Unhealthy", node.GetProperty("health").GetString());
    }
}
