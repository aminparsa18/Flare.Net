using Flare.Api.DockerResources;
using Flare.Api.Model;
using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.DockerResources;

/// <summary>
/// Tests for <see cref="DockerContainerPoller"/>'s pure label/state-mapping logic - not the
/// live polling loop itself (no fake Docker Engine API server here), same scope as
/// <c>LiveTail</c>'s own test folder (JSON context + pure mapping, not
/// <c>LogTailBroadcaster</c>'s background loop).
/// </summary>
public class DockerContainerPollerTests
{
    private static DockerContainerInspect MakeContainer(
        string id = "abc123def456extra",
        string name = "/flare-net-ingest-1",
        string? role = "ingest",
        string? relationships = null,
        string? status = "running",
        string? healthStatus = null,
        Dictionary<string, List<DockerPortBinding>?>? ports = null)
    {
        var labels = new Dictionary<string, string>();
        if (role is not null)
        {
            labels["flare.role"] = role;
        }

        if (relationships is not null)
        {
            labels["flare.relationships"] = relationships;
        }

        return new DockerContainerInspect
        {
            Id = id,
            Name = name,
            Config = new DockerContainerConfig { Image = "xracer007/flare-ingest:edge", Labels = labels },
            State = new DockerContainerState { Status = status, Health = healthStatus is null ? null : new DockerContainerHealth { Status = healthStatus } },
            NetworkSettings = new DockerNetworkSettings { Ports = ports },
        };
    }

    [Fact]
    public void BuildSnapshot_MapsRoleNameImageStateFromLabelsAndInspect()
    {
        var container = MakeContainer(id: "abc123def456extra", name: "/flare-net-ingest-1", role: "ingest", status: "running");

        var snapshot = DockerContainerPoller.BuildSnapshot([container], TimeProvider.System);

        var node = Assert.Single(snapshot.Nodes);
        Assert.Equal("abc123def456", node.Id); // Docker's conventional 12-char short ID.
        Assert.Equal("ingest", node.Role);
        Assert.Equal("flare-net-ingest-1", node.Name); // leading "/" stripped.
        Assert.Equal("xracer007/flare-ingest:edge", node.Image);
        Assert.Equal(ResourceState.Running, node.State);
        Assert.Null(node.Health);
        Assert.True(snapshot.Available);
        Assert.NotNull(snapshot.UpdatedAt);
    }

    [Fact]
    public void BuildSnapshot_SkipsContainers_MissingFlareRoleLabel()
    {
        var container = MakeContainer(role: null);

        var snapshot = DockerContainerPoller.BuildSnapshot([container], TimeProvider.System);

        Assert.Empty(snapshot.Nodes);
    }

    [Fact]
    public void BuildSnapshot_ParsesRelationshipsLabel_IntoEdgesSourcedFromTheOwningRole()
    {
        var ingest = MakeContainer(id: "111111111111", role: "ingest", relationships: "clickhouse:Reference,redis:Reference");
        var dashboard = MakeContainer(id: "222222222222", name: "/flare-net-dashboard-1", role: "dashboard", relationships: "api:Reference");

        var snapshot = DockerContainerPoller.BuildSnapshot([ingest, dashboard], TimeProvider.System);

        Assert.Equal(3, snapshot.Edges.Count);
        Assert.Contains(snapshot.Edges, e => e is { SourceRole: "ingest", TargetRole: "clickhouse", RelationshipType: "Reference" });
        Assert.Contains(snapshot.Edges, e => e is { SourceRole: "ingest", TargetRole: "redis", RelationshipType: "Reference" });
        Assert.Contains(snapshot.Edges, e => e is { SourceRole: "dashboard", TargetRole: "api", RelationshipType: "Reference" });
    }

    [Fact]
    public void BuildSnapshot_SkipsMalformedRelationshipEntries_WithoutFailingTheWholeLabel()
    {
        var container = MakeContainer(relationships: "clickhouse:Reference,not-a-valid-entry,redis:Reference,:MissingSource,noType:");

        var snapshot = DockerContainerPoller.BuildSnapshot([container], TimeProvider.System);

        Assert.Equal(2, snapshot.Edges.Count);
        Assert.Contains(snapshot.Edges, e => e.TargetRole == "clickhouse");
        Assert.Contains(snapshot.Edges, e => e.TargetRole == "redis");
    }

    [Theory]
    [InlineData("running", ResourceState.Running)]
    [InlineData("exited", ResourceState.Exited)]
    [InlineData("restarting", ResourceState.Restarting)]
    [InlineData("paused", ResourceState.Paused)]
    [InlineData("dead", ResourceState.Unknown)]
    [InlineData(null, ResourceState.Unknown)]
    public void ParseState_MapsDockerStatusStrings(string? status, ResourceState expected) =>
        Assert.Equal(expected, DockerContainerPoller.ParseState(status));

    [Theory]
    [InlineData("starting", ResourceHealth.Starting)]
    [InlineData("healthy", ResourceHealth.Healthy)]
    [InlineData("unhealthy", ResourceHealth.Unhealthy)]
    [InlineData(null, null)]
    [InlineData("something-unrecognized", null)]
    public void ParseHealth_MapsDockerHealthStrings_OrNullWhenNoHealthcheckConfigured(string? status, ResourceHealth? expected) =>
        Assert.Equal(expected, DockerContainerPoller.ParseHealth(status));

    [Fact]
    public void BuildUrls_BuildsLocalhostUrls_FromDistinctPublishedHostPorts()
    {
        var ports = new Dictionary<string, List<DockerPortBinding>?>
        {
            ["8123/tcp"] = [new DockerPortBinding { HostIp = "0.0.0.0", HostPort = "8123" }],
            ["9000/tcp"] = null, // unpublished - Docker represents this as a null binding list.
            ["8123/udp"] = [new DockerPortBinding { HostIp = "0.0.0.0", HostPort = "8123" }], // duplicate host port, different proto.
        };

        var urls = DockerContainerPoller.BuildUrls(ports);

        Assert.Equal(["http://localhost:8123"], urls);
    }

    [Fact]
    public void BuildUrls_ReturnsEmpty_WhenNoPortsPublished() =>
        Assert.Empty(DockerContainerPoller.BuildUrls(null));

    [Fact]
    public void BuildProducerOverlay_MapsEachActiveService_ToANamespacedNode_WithAnEdgeIntoIngest()
    {
        var active = new[]
        {
            new ActiveService("log-generator", new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero)),
        };

        var (producers, edges) = DockerContainerPoller.BuildProducerOverlay(active);

        var producer = Assert.Single(producers);
        Assert.Equal("service:log-generator", producer.Id);
        Assert.Equal("log-generator", producer.ServiceName);
        Assert.Equal(new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero), producer.LastSeenAt);

        var edge = Assert.Single(edges);
        Assert.Equal("service:log-generator", edge.SourceRole);
        Assert.Equal("ingest", edge.TargetRole);
        Assert.Equal("Producer", edge.RelationshipType);
    }

    [Fact]
    public void BuildProducerOverlay_NamespacesIds_SoAProducerCanNeverCollideWithADockerRole()
    {
        // A producer literally named "api" must not collide with the real Docker "api"
        // node's id - see ProducerServiceDto.Id's remarks.
        var active = new[] { new ActiveService("api", DateTimeOffset.UnixEpoch) };

        var (producers, _) = DockerContainerPoller.BuildProducerOverlay(active);

        Assert.Equal("service:api", Assert.Single(producers).Id);
    }

    [Fact]
    public void BuildProducerOverlay_ReturnsEmpty_WhenNoActiveServices() =>
        Assert.Empty(DockerContainerPoller.BuildProducerOverlay([]).Producers);
}
