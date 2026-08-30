using Flare.Api.Query;
using Flare.Api.ResourceGraph;
using Xunit;

namespace Flare.Api.Tests.ResourceGraph;

/// <summary>
/// Tests for <see cref="ProducerOverlayBuilder"/> - extracted from
/// <c>DockerResources.DockerContainerPoller</c> since it's shared by both topology
/// providers (see that type's own remarks).
/// </summary>
public class ProducerOverlayBuilderTests
{
    [Fact]
    public void Build_MapsEachActiveService_ToANamespacedNode_WithAnEdgeIntoIngest()
    {
        var active = new[]
        {
            new ActiveService("log-generator", new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero)),
        };

        var (producers, edges) = ProducerOverlayBuilder.Build(active);

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
    public void Build_NamespacesIds_SoAProducerCanNeverCollideWithARoleId()
    {
        // A producer literally named "api" must not collide with the real "api" node's id
        // from either provider - see ProducerServiceDto.Id's remarks.
        var active = new[] { new ActiveService("api", DateTimeOffset.UnixEpoch) };

        var (producers, _) = ProducerOverlayBuilder.Build(active);

        Assert.Equal("service:api", Assert.Single(producers).Id);
    }

    [Fact]
    public void Build_ReturnsEmpty_WhenNoActiveServices() =>
        Assert.Empty(ProducerOverlayBuilder.Build([]).Producers);
}
