using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Flare.Api.Auth;
using Flare.Api.Endpoints;
using Flare.Api.KubernetesResources;
using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Api.ResourceGraph;
using Flare.Identity.Projects;
using Flare.Identity.Users;
using k8s.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Api.Tests.ResourceGraph;

/// <summary>
/// The Resources page's snapshot and watch stream are built once by a background poller, where
/// <see cref="ServiceScope.Current"/> is null, so they must be narrowed to the caller's projects
/// per request (ADR-0123). Runs the real <see cref="ProjectScopeMiddleware"/>, poller snapshot
/// builder, <see cref="ProducerOverlayBuilder"/> and snapshot handler; only ClickHouse and the
/// project store are faked.
/// </summary>
public class ResourceGraphProjectScopeTests
{
    private static readonly Guid MemberOfA = Guid.NewGuid();
    private static readonly Guid ProjectA = Guid.NewGuid();
    private static readonly Guid ProjectB = Guid.NewGuid();

    private static readonly string[] ProjectAServices = ["shop-web", "shop-api"];
    private static readonly string[] ProjectBServices = ["payroll-api", "payroll-salary-export", "hr-compensation"];

    private sealed class ProjectsFake : IProjectStore
    {
        public Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Project>>(
        [
            new Project(ProjectA, "A", "", DateTimeOffset.UtcNow, ["shop-*"]),
            new Project(ProjectB, "B", "", DateTimeOffset.UtcNow, ["payroll-*", "hr-compensation"]),
        ]);

        public Task<IReadOnlyList<ProjectMembership>> ListMembershipsAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ProjectMembership>>(userId == MemberOfA ? [new ProjectMembership(ProjectA, UserRole.Member)] : []);

        public Task<Project?> GetAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Project> CreateAsync(string n, string d, IReadOnlyList<string> p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Project?> UpdateAsync(Guid id, string n, string d, IReadOnlyList<string> p, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> DeleteAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ProjectMember>> ListMembersAsync(Guid projectId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> SetMemberAsync(Guid projectId, Guid userId, UserRole role, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> RemoveMemberAsync(Guid projectId, Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Stands in for ClickHouse: applies the allow-list the real WHERE clause applies, from the ambient scope at call time.</summary>
    public class ActiveServicesProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod!.Name != nameof(ILogQueryService.GetActiveServiceNamesAsync))
            {
                throw new NotSupportedException(targetMethod.Name);
            }

            IReadOnlyList<ActiveService> rows = [.. ProjectAServices.Concat(ProjectBServices)
                .Where(ServiceScope.Allows)
                .Select(s => new ActiveService(s, DateTimeOffset.UtcNow))];
            return Task.FromResult(rows);
        }
    }

    private static DefaultHttpContext CreateContext(Guid userId, UserRole role)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role.ToString())],
            authenticationType: "FlareSession");
        return new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider(),
            Response = { Body = new MemoryStream() },
        };
    }

    /// <summary>What a topology poller does from a BackgroundService: no request, so no ambient scope.</summary>
    private static async Task<ResourceGraphSourceRegistry> PollerPublishedRegistryAsync()
    {
        var registry = new ResourceGraphSourceRegistry();
        var logQuery = DispatchProxy.Create<ILogQueryService, ActiveServicesProxy>();
        var flarePods = new List<V1Pod>
        {
            new()
            {
                Metadata = new V1ObjectMeta { Name = "flare-ingest-0", Labels = new Dictionary<string, string> { ["flare.resource"] = "true", ["flare.role"] = "ingest", ["app"] = "ingest" } },
                Spec = new V1PodSpec { Containers = [new V1Container { Name = "c", Image = "img" }] },
                Status = new V1PodStatus { Phase = "Running" },
            },
        };
        var services = new List<V1Service>
        {
            new() { Metadata = new V1ObjectMeta { Name = "flare-ingest" }, Spec = new V1ServiceSpec { Selector = new Dictionary<string, string> { ["app"] = "ingest" } } },
        };

        await Task.Run(async () =>
        {
            Assert.Null(ServiceScope.Current);
            var snapshot = KubernetesResourcePoller.BuildSnapshot(flarePods, services, "flare-ns", TimeProvider.System);
            var active = await logQuery.GetActiveServiceNamesAsync(TimeSpan.FromMinutes(5), CancellationToken.None);
            var (producers, edges) = ProducerOverlayBuilder.Build(active);
            registry.Publish("Kubernetes", snapshot with { Producers = producers, Edges = [.. snapshot.Edges, .. edges] });
        });
        return registry;
    }

    /// <summary>Runs <paramref name="action"/> inside the real project-scope middleware, so <see cref="ServiceScope.Current"/> is what a request would see.</summary>
    private static async Task InScopeAsync(DefaultHttpContext context, Func<HttpContext, Task> action) =>
        await new ProjectScopeMiddleware(c => action(c)).InvokeAsync(context, new ProjectsFake());

    private static async Task<JsonElement> SnapshotAsync(DefaultHttpContext context, ResourceGraphSourceRegistry registry)
    {
        await InScopeAsync(context, async c => await ResourceGraphEndpoints.HandleSnapshot(c, registry).ExecuteAsync(c));
        context.Response.Body.Position = 0;
        return JsonDocument.Parse(await new StreamReader(context.Response.Body).ReadToEndAsync()).RootElement;
    }

    [Fact]
    public async Task Snapshot_ForMemberOfOneProject_OmitsOtherProjectsProducersAndTheirEdges()
    {
        var registry = await PollerPublishedRegistryAsync();

        var root = await SnapshotAsync(CreateContext(MemberOfA, UserRole.Member), registry);

        var producers = root.GetProperty("producers").EnumerateArray().Select(p => p.GetProperty("serviceName").GetString()!).ToArray();
        var edgeSources = root.GetProperty("edges").EnumerateArray().Select(e => e.GetProperty("sourceRole").GetString()!).ToArray();
        Assert.Equal(ProjectAServices.OrderBy(x => x), producers.OrderBy(x => x));
        Assert.DoesNotContain(edgeSources, s => ProjectBServices.Any(b => s == "service:" + b));
        Assert.Equal(ProjectAServices.Select(s => "service:" + s).OrderBy(x => x), edgeSources.Where(s => s.StartsWith("service:", StringComparison.Ordinal)).OrderBy(x => x));
    }

    [Fact]
    public async Task Snapshot_ForMemberOfOneProject_StillShowsFlaresOwnTopology()
    {
        var registry = await PollerPublishedRegistryAsync();

        var root = await SnapshotAsync(CreateContext(MemberOfA, UserRole.Member), registry);

        Assert.Contains(root.GetProperty("nodes").EnumerateArray(), n => n.GetProperty("role").GetString() == "ingest");
    }

    [Fact]
    public async Task Snapshot_ForAdmin_ShowsEveryProducer()
    {
        var registry = await PollerPublishedRegistryAsync();

        var root = await SnapshotAsync(CreateContext(Guid.NewGuid(), UserRole.Admin), registry);

        var producers = root.GetProperty("producers").EnumerateArray().Select(p => p.GetProperty("serviceName").GetString()!);
        Assert.Equal(ProjectAServices.Concat(ProjectBServices).OrderBy(x => x), producers.OrderBy(x => x));
    }

    [Fact]
    public async Task Snapshot_ForUserInNoProject_HasNoProducers()
    {
        var registry = await PollerPublishedRegistryAsync();

        var root = await SnapshotAsync(CreateContext(Guid.NewGuid(), UserRole.Member), registry);

        Assert.Equal(0, root.GetProperty("producers").GetArrayLength());
        Assert.DoesNotContain(root.GetProperty("edges").EnumerateArray(), e => e.GetProperty("sourceRole").GetString()!.StartsWith("service:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Watch_ScopeIsCapturedAtSubscribeTime_ForTheFirstAndEveryLaterPush()
    {
        var registry = await PollerPublishedRegistryAsync();
        ResourceGraphSubscription? subscription = null;

        await InScopeAsync(CreateContext(MemberOfA, UserRole.Member), _ =>
        {
            subscription = registry.Subscribe();
            return Task.CompletedTask;
        });

        // The poller publishes again later, from a context with no ambient scope.
        var fresh = new ResourceGraphSnapshot
        {
            Available = true,
            Producers = [.. ProjectAServices.Concat(ProjectBServices).Select(s => new ProducerServiceDto { Id = "service:" + s, ServiceName = s, LastSeenAt = DateTimeOffset.UtcNow })],
        };
        registry.Publish("Kubernetes", fresh);

        Assert.True(subscription!.Reader.TryRead(out var latest));
        Assert.Equal(ProjectAServices.OrderBy(x => x), latest!.Producers.Select(p => p.ServiceName).OrderBy(x => x));
    }

    [Fact]
    public async Task Watch_FirstPush_IsAlreadyScoped()
    {
        var registry = await PollerPublishedRegistryAsync();
        ResourceGraphSubscription? subscription = null;

        await InScopeAsync(CreateContext(MemberOfA, UserRole.Member), _ =>
        {
            subscription = registry.Subscribe();
            return Task.CompletedTask;
        });

        Assert.True(subscription!.Reader.TryRead(out var first));
        Assert.Empty(first!.Producers.Select(p => p.ServiceName).Intersect(ProjectBServices));
        Assert.DoesNotContain(first.Edges, e => ProjectBServices.Any(b => e.SourceRole == "service:" + b));
    }
}
