using Flare.Api.KubernetesResources;
using Flare.Api.Model;
using k8s.Models;
using Xunit;

namespace Flare.Api.Tests.KubernetesResources;

/// <summary>
/// Tests for <see cref="KubernetesResourcePoller"/>'s pure Pod/Service-mapping logic - not
/// the live polling loop itself (no fake Kubernetes API server here), same scope as
/// <c>DockerResources.DockerContainerPollerTests</c>.
/// </summary>
public class KubernetesResourcePollerTests
{
    private static V1Pod MakePod(
        string name,
        string? role,
        string? relationships = null,
        string? phase = "Running",
        string image = "xracer007/flare-ingest:edge",
        bool? ready = true)
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

        return new V1Pod
        {
            Metadata = new V1ObjectMeta { Name = name, Labels = labels },
            Spec = new V1PodSpec { Containers = [new V1Container { Name = name, Image = image }] },
            Status = new V1PodStatus
            {
                Phase = phase,
                ContainerStatuses = ready is null ? [] : [new V1ContainerStatus { Name = name, Ready = ready.Value, State = new V1ContainerState() }],
            },
        };
    }

    private static V1Service MakeService(string name, Dictionary<string, string>? selector) =>
        new() { Metadata = new V1ObjectMeta { Name = name }, Spec = new V1ServiceSpec { Selector = selector } };

    [Fact]
    public void BuildSnapshot_BuildsNamespaceRoot_AndOneDeploymentGroupPerRole_WithPodsNested()
    {
        var ingest = MakePod("flare-net-ingest-abc123", "ingest");

        var snapshot = KubernetesResourcePoller.BuildSnapshot([ingest], [], "flare-ns", TimeProvider.System);

        var ns = Assert.Single(snapshot.Nodes, n => n.Kind == "Namespace");
        Assert.Equal("flare-ns", ns.Name);
        Assert.Null(ns.ParentId);

        var deployment = Assert.Single(snapshot.Nodes, n => n.Kind == "Deployment");
        Assert.Equal("ingest", deployment.Name);
        Assert.Equal(ns.Id, deployment.ParentId);

        var pod = Assert.Single(snapshot.Nodes, n => n.Kind == "Pod");
        Assert.Equal("flare-net-ingest-abc123", pod.Id);
        Assert.Equal("flare-net-ingest-abc123", pod.Name);
        Assert.Equal("ingest", pod.Role); // Role, not Id - see ResourceNodeDto.Role's remarks.
        Assert.Equal("xracer007/flare-ingest:edge", pod.Image);
        Assert.Equal(deployment.Id, pod.ParentId);
        Assert.Equal(ResourceState.Running, pod.State);
        Assert.Equal(ResourceHealth.Healthy, pod.Health);
        Assert.True(snapshot.Available);
        Assert.Equal("Kubernetes", snapshot.Provider);
    }

    [Fact]
    public void BuildSnapshot_SkipsPods_MissingFlareRoleLabel()
    {
        var pod = MakePod("orphan", role: null);

        var snapshot = KubernetesResourcePoller.BuildSnapshot([pod], [], "flare-ns", TimeProvider.System);

        Assert.DoesNotContain(snapshot.Nodes, n => n.Kind == "Pod");
    }

    [Fact]
    public void BuildSnapshot_GroupsMultipleRoles_IntoSeparateDeploymentNodes()
    {
        var ingest = MakePod("ingest-1", "ingest");
        var api = MakePod("api-1", "api");

        var snapshot = KubernetesResourcePoller.BuildSnapshot([ingest, api], [], "flare-ns", TimeProvider.System);

        Assert.Equal(2, snapshot.Nodes.Count(n => n.Kind == "Deployment"));
        Assert.Equal(2, snapshot.Nodes.Count(n => n.Kind == "Pod"));
    }

    [Fact]
    public void BuildSnapshot_ParsesRelationshipsLabel_IntoEdgesSourcedFromTheOwningRole()
    {
        var ingest = MakePod("ingest-1", "ingest", relationships: "clickhouse:Reference,redis:Reference");
        var dashboard = MakePod("dashboard-1", "dashboard", relationships: "api:Reference");

        var snapshot = KubernetesResourcePoller.BuildSnapshot([ingest, dashboard], [], "flare-ns", TimeProvider.System);

        Assert.Equal(3, snapshot.Edges.Count);
        Assert.Contains(snapshot.Edges, e => e is { SourceRole: "ingest", TargetRole: "clickhouse", RelationshipType: "Reference" });
        Assert.Contains(snapshot.Edges, e => e is { SourceRole: "ingest", TargetRole: "redis", RelationshipType: "Reference" });
        Assert.Contains(snapshot.Edges, e => e is { SourceRole: "dashboard", TargetRole: "api", RelationshipType: "Reference" });
    }

    [Fact]
    public void BuildSnapshot_AddsServiceNode_WithSelectsEdge_WhenSelectorMatchesAFlareRolePod()
    {
        var api = MakePod("api-1", "api");
        var service = MakeService("flare-api-service", new Dictionary<string, string> { ["flare.role"] = "api" });

        var snapshot = KubernetesResourcePoller.BuildSnapshot([api], [service], "flare-ns", TimeProvider.System);

        var serviceNode = Assert.Single(snapshot.Nodes, n => n.Kind == "Service");
        Assert.Equal("flare-api-service", serviceNode.Name);
        Assert.StartsWith("k8s-service:", serviceNode.Id);
        Assert.Contains(snapshot.Edges, e => e is { SourceRole: "k8s-service:flare-api-service", TargetRole: "api", RelationshipType: "Selects" });
    }

    [Fact]
    public void BuildSnapshot_OmitsServiceNode_WhenSelectorMatchesNoFlareRolePod()
    {
        var api = MakePod("api-1", "api");
        var unrelated = MakeService("some-other-service", new Dictionary<string, string> { ["app"] = "not-flare" });

        var snapshot = KubernetesResourcePoller.BuildSnapshot([api], [unrelated], "flare-ns", TimeProvider.System);

        Assert.DoesNotContain(snapshot.Nodes, n => n.Kind == "Service");
    }

    [Fact]
    public void BuildSnapshot_OmitsServiceNode_WhenSelectorIsEmptyOrMissing()
    {
        var api = MakePod("api-1", "api");
        var headless = MakeService("headless", selector: null);

        var snapshot = KubernetesResourcePoller.BuildSnapshot([api], [headless], "flare-ns", TimeProvider.System);

        Assert.DoesNotContain(snapshot.Nodes, n => n.Kind == "Service");
    }

    [Fact]
    public void BuildSnapshot_NeverCollides_K8sServiceIdWithProducerServiceId()
    {
        // A real Kubernetes Service literally named "api" must not collide with a producer
        // overlay node id ("service:api") - see ProducerServiceDto.Id's remarks and
        // KubernetesResourcePoller.BuildSnapshot's "k8s-service:" prefix comment.
        var api = MakePod("api-1", "api");
        var service = MakeService("api", new Dictionary<string, string> { ["flare.role"] = "api" });

        var snapshot = KubernetesResourcePoller.BuildSnapshot([api], [service], "flare-ns", TimeProvider.System);

        var serviceNode = Assert.Single(snapshot.Nodes, n => n.Kind == "Service");
        Assert.Equal("k8s-service:api", serviceNode.Id);
        Assert.NotEqual("service:api", serviceNode.Id);
    }

    [Theory]
    [InlineData("Running", ResourceState.Running)]
    [InlineData("Succeeded", ResourceState.Exited)]
    [InlineData("Failed", ResourceState.Exited)]
    [InlineData("Pending", ResourceState.Unknown)]
    [InlineData(null, ResourceState.Unknown)]
    public void ParsePhase_MapsPodPhaseStrings(string? phase, ResourceState expected) =>
        Assert.Equal(expected, KubernetesResourcePoller.ParsePhase(phase));

    [Fact]
    public void ParseHealth_ReturnsNull_WhenNoContainerStatusesReported() =>
        Assert.Null(KubernetesResourcePoller.ParseHealth(new V1PodStatus { Phase = "Running", ContainerStatuses = [] }));

    [Fact]
    public void ParseHealth_ReturnsStarting_WhenPodStillPending() =>
        Assert.Equal(
            ResourceHealth.Starting,
            KubernetesResourcePoller.ParseHealth(new V1PodStatus
            {
                Phase = "Pending",
                ContainerStatuses = [new V1ContainerStatus { Name = "c", Ready = false, State = new V1ContainerState() }],
            }));

    [Fact]
    public void ParseHealth_ReturnsHealthy_WhenEveryContainerIsReady() =>
        Assert.Equal(
            ResourceHealth.Healthy,
            KubernetesResourcePoller.ParseHealth(new V1PodStatus
            {
                Phase = "Running",
                ContainerStatuses = [new V1ContainerStatus { Name = "c", Ready = true, State = new V1ContainerState() }],
            }));

    [Fact]
    public void ParseHealth_ReturnsUnhealthy_WhenAnyContainerIsNotReady() =>
        Assert.Equal(
            ResourceHealth.Unhealthy,
            KubernetesResourcePoller.ParseHealth(new V1PodStatus
            {
                Phase = "Running",
                ContainerStatuses =
                [
                    new V1ContainerStatus { Name = "a", Ready = true, State = new V1ContainerState() },
                    new V1ContainerStatus { Name = "b", Ready = false, State = new V1ContainerState() },
                ],
            }));

    [Fact]
    public void MatchesSelector_RequiresEveryKeyValuePair_ToBePresentInPodLabels()
    {
        var podLabels = new Dictionary<string, string> { ["flare.role"] = "api", ["extra"] = "1" };
        var selector = new Dictionary<string, string> { ["flare.role"] = "api" };

        Assert.True(KubernetesResourcePoller.MatchesSelector(podLabels, selector));
        Assert.False(KubernetesResourcePoller.MatchesSelector(podLabels, new Dictionary<string, string> { ["flare.role"] = "ingest" }));
        Assert.False(KubernetesResourcePoller.MatchesSelector(null, selector));
    }
}
