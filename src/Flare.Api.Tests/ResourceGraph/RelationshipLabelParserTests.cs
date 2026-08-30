using Flare.Api.ResourceGraph;
using Xunit;

namespace Flare.Api.Tests.ResourceGraph;

/// <summary>
/// Tests for <see cref="RelationshipLabelParser"/> - extracted from
/// <c>DockerResources.DockerContainerPoller</c> since it's shared by both topology
/// providers (see that type's own remarks). <c>DockerContainerPollerTests</c>' own
/// relationship tests still cover this indirectly through <c>BuildSnapshot</c>.
/// </summary>
public class RelationshipLabelParserTests
{
    [Fact]
    public void Parse_SplitsCommaSeparatedEntries_IntoOneEdgePerEntry()
    {
        var labels = new Dictionary<string, string> { ["flare.relationships"] = "clickhouse:Reference,redis:Reference" };

        var edges = RelationshipLabelParser.Parse("ingest", labels).ToList();

        Assert.Equal(2, edges.Count);
        Assert.Contains(edges, e => e is { SourceRole: "ingest", TargetRole: "clickhouse", RelationshipType: "Reference" });
        Assert.Contains(edges, e => e is { SourceRole: "ingest", TargetRole: "redis", RelationshipType: "Reference" });
    }

    [Fact]
    public void Parse_SkipsMalformedEntries_WithoutFailingTheWholeLabel()
    {
        var labels = new Dictionary<string, string> { ["flare.relationships"] = "clickhouse:Reference,not-a-valid-entry,redis:Reference,:MissingSource,noType:" };

        var edges = RelationshipLabelParser.Parse("ingest", labels).ToList();

        Assert.Equal(2, edges.Count);
        Assert.Contains(edges, e => e.TargetRole == "clickhouse");
        Assert.Contains(edges, e => e.TargetRole == "redis");
    }

    [Fact]
    public void Parse_ReturnsEmpty_WhenLabelMissing() =>
        Assert.Empty(RelationshipLabelParser.Parse("ingest", new Dictionary<string, string>()));

    [Fact]
    public void Parse_ReturnsEmpty_WhenLabelBlank() =>
        Assert.Empty(RelationshipLabelParser.Parse("ingest", new Dictionary<string, string> { ["flare.relationships"] = "   " }));
}
