using System.Security.Claims;
using System.Text.Json;
using Flare.Api.Endpoints;
using Flare.Api.Model;
using Flare.Api.Tests.TestSupport;
using Flare.Identity.DashboardPins;
using Flare.Identity.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Api.Tests.Endpoints;

/// <summary>
/// Exercises <see cref="DashboardEndpoints"/>' ownership-check logic (ADR-0027) directly
/// against a <see cref="FakeDashboardQueryService"/> - same "handlers made internal via
/// InternalsVisibleTo, executed via <see cref="IResult.ExecuteAsync"/> against a real
/// <see cref="DefaultHttpContext"/>, no ClickHouse involved" convention as
/// <see cref="AuthEndpointsTests"/>.
/// </summary>
public class DashboardEndpointsTests
{
    private static readonly IServiceProvider EmptyRequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();

    private static DefaultHttpContext CreateContext(object? jsonBody = null)
    {
        var context = new DefaultHttpContext { RequestServices = EmptyRequestServices };
        if (jsonBody is not null)
        {
            context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(jsonBody)));
        }
        context.Response.Body = new MemoryStream();
        return context;
    }

    /// <summary>Mirrors exactly what SessionAuthenticationHandler builds for a real request.</summary>
    private static ClaimsPrincipal AuthenticatedPrincipal(Guid userId, UserRole role = UserRole.Member)
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, role.ToString())],
            authenticationType: "FlareSession");
        return new ClaimsPrincipal(identity);
    }

    private static readonly ClaimsPrincipal Unauthenticated = new(new ClaimsIdentity());

    private static Dashboard SeedDashboard(FakeDashboardQueryService dashboards, Guid? ownerUserId)
    {
        var dashboard = new Dashboard
        {
            Id = Guid.NewGuid(),
            Name = "Existing",
            OwnerUserId = ownerUserId,
            LayoutJson = JsonDocument.Parse("{}").RootElement,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        dashboards.Seed(dashboard);
        return dashboard;
    }

    private static object UpdateBody(string name = "Renamed") => new { name, description = "", layoutJson = new { } };

    [Fact]
    public async Task Create_SetsOwnerUserId_ToTheAuthenticatedCaller()
    {
        var dashboards = new FakeDashboardQueryService();
        var ownerId = Guid.NewGuid();
        var context = CreateContext(new { name = "New", description = "", layoutJson = new { } });
        context.User = AuthenticatedPrincipal(ownerId);

        var result = await DashboardEndpoints.HandleCreateAsync(context, context.User, dashboards, new FakeProjectStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
        var created = Assert.Single(await dashboards.ListAsync(CancellationToken.None));
        Assert.Equal(ownerId, created.OwnerUserId);
    }

    [Fact]
    public async Task Create_LeavesOwnerUserIdNull_WhenThePrincipalIsNotAuthenticated()
    {
        // The "Flare's opt-in auth is disabled entirely" case - RequireMember lets the
        // request through unauthenticated, so there's no caller to attribute it to.
        var dashboards = new FakeDashboardQueryService();
        var context = CreateContext(new { name = "New", description = "", layoutJson = new { } });
        context.User = Unauthenticated;

        var result = await DashboardEndpoints.HandleCreateAsync(context, context.User, dashboards, new FakeProjectStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status201Created, context.Response.StatusCode);
        var created = Assert.Single(await dashboards.ListAsync(CancellationToken.None));
        Assert.Null(created.OwnerUserId);
    }

    [Fact]
    public async Task Update_Succeeds_ForTheOwningUser()
    {
        var dashboards = new FakeDashboardQueryService();
        var ownerId = Guid.NewGuid();
        var dashboard = SeedDashboard(dashboards, ownerId);
        var context = CreateContext(UpdateBody());
        context.User = AuthenticatedPrincipal(ownerId);

        var result = await DashboardEndpoints.HandleUpdateAsync(dashboard.Id, context, context.User, dashboards, new FakeProjectStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsForbidden_ForAMemberWhoDoesNotOwnTheDashboard()
    {
        // Asserts the IResult itself rather than executing it - Results.Forbid()'s
        // ExecuteAsync resolves IAuthenticationService off RequestServices, which needs a
        // real scheme registered to mean anything (see
        // ConditionalAuthorizationMiddlewareResultHandlerTests for that setup); the branch
        // under test here is CanMutate returning false, not ForbidHttpResult's own wire
        // behavior.
        var dashboards = new FakeDashboardQueryService();
        var dashboard = SeedDashboard(dashboards, Guid.NewGuid());
        var context = CreateContext(UpdateBody());
        context.User = AuthenticatedPrincipal(Guid.NewGuid()); // a different Member

        var result = await DashboardEndpoints.HandleUpdateAsync(dashboard.Id, context, context.User, dashboards, new FakeProjectStore(), CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(result);
    }

    [Fact]
    public async Task Update_Succeeds_ForAnAdmin_EvenWhenNotTheOwner()
    {
        var dashboards = new FakeDashboardQueryService();
        var dashboard = SeedDashboard(dashboards, Guid.NewGuid());
        var context = CreateContext(UpdateBody());
        context.User = AuthenticatedPrincipal(Guid.NewGuid(), UserRole.Admin);

        var result = await DashboardEndpoints.HandleUpdateAsync(dashboard.Id, context, context.User, dashboards, new FakeProjectStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Update_Succeeds_ForAnyMember_WhenTheDashboardIsUnowned()
    {
        // Predates OwnerUserId, or was created while auth was off - see ADR-0027.
        var dashboards = new FakeDashboardQueryService();
        var dashboard = SeedDashboard(dashboards, ownerUserId: null);
        var context = CreateContext(UpdateBody());
        context.User = AuthenticatedPrincipal(Guid.NewGuid());

        var result = await DashboardEndpoints.HandleUpdateAsync(dashboard.Id, context, context.User, dashboards, new FakeProjectStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenTheDashboardDoesNotExist()
    {
        var dashboards = new FakeDashboardQueryService();
        var context = CreateContext(UpdateBody());
        context.User = AuthenticatedPrincipal(Guid.NewGuid());

        var result = await DashboardEndpoints.HandleUpdateAsync(Guid.NewGuid(), context, context.User, dashboards, new FakeProjectStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsForbidden_ForAMemberWhoDoesNotOwnTheDashboard()
    {
        // See Update_ReturnsForbidden_ForAMemberWhoDoesNotOwnTheDashboard's remarks on why
        // this asserts the IResult rather than executing it.
        var dashboards = new FakeDashboardQueryService();
        var dashboard = SeedDashboard(dashboards, Guid.NewGuid());
        var context = CreateContext();
        context.User = AuthenticatedPrincipal(Guid.NewGuid());

        var result = await DashboardEndpoints.HandleDeleteAsync(dashboard.Id, context, context.User, dashboards, CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult>(result);
        Assert.NotNull(await dashboards.GetAsync(dashboard.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_Succeeds_ForTheOwningUser()
    {
        var dashboards = new FakeDashboardQueryService();
        var ownerId = Guid.NewGuid();
        var dashboard = SeedDashboard(dashboards, ownerId);
        var context = CreateContext();
        context.User = AuthenticatedPrincipal(ownerId);

        var result = await DashboardEndpoints.HandleDeleteAsync(dashboard.Id, context, context.User, dashboards, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status204NoContent, context.Response.StatusCode);
        Assert.Null(await dashboards.GetAsync(dashboard.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Create_RejectsTooManyTags()
    {
        var dashboards = new FakeDashboardQueryService();
        var tags = Enumerable.Range(0, DashboardTags.MaxTags + 1).Select(i => $"t{i}").ToArray();
        var context = CreateContext(new { name = "New", description = "", layoutJson = new { }, tags });
        context.User = AuthenticatedPrincipal(Guid.NewGuid());

        var result = await DashboardEndpoints.HandleCreateAsync(context, context.User, dashboards, new FakeProjectStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Empty(await dashboards.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Pin_ForUnknownDashboard_Returns404()
    {
        var context = CreateContext();
        context.User = AuthenticatedPrincipal(Guid.NewGuid());
        var pins = new InMemoryPinStore();

        var result = await DashboardEndpoints.HandlePinAsync(Guid.NewGuid(), context, context.User, new FakeDashboardQueryService(), pins, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        Assert.Empty(pins.Pins);
    }

    [Fact]
    public async Task Pin_ThenUnpin_PinsForTheCallerOnly_ViewerIncluded()
    {
        var dashboards = new FakeDashboardQueryService();
        var dashboard = SeedDashboard(dashboards, Guid.NewGuid());
        var viewerId = Guid.NewGuid();
        var principal = AuthenticatedPrincipal(viewerId, UserRole.Viewer);
        var pins = new InMemoryPinStore();

        var pinContext = CreateContext();
        await (await DashboardEndpoints.HandlePinAsync(dashboard.Id, pinContext, principal, dashboards, pins, CancellationToken.None)).ExecuteAsync(pinContext);

        Assert.Equal(StatusCodes.Status204NoContent, pinContext.Response.StatusCode);
        Assert.Equal([dashboard.Id], await pins.ListAsync(viewerId));
        Assert.Empty(await pins.ListAsync(Guid.NewGuid()));

        await (await DashboardEndpoints.HandleUnpinAsync(dashboard.Id, principal, pins, CancellationToken.None)).ExecuteAsync(CreateContext());
        Assert.Empty(await pins.ListAsync(viewerId));
    }

    [Fact]
    public async Task Pin_WithAuthDisabled_UsesTheSharedEmptyGuidOwner()
    {
        var dashboards = new FakeDashboardQueryService();
        var dashboard = SeedDashboard(dashboards, null);
        var pins = new InMemoryPinStore();

        await (await DashboardEndpoints.HandlePinAsync(dashboard.Id, CreateContext(), Unauthenticated, dashboards, pins, CancellationToken.None)).ExecuteAsync(CreateContext());

        Assert.Equal([dashboard.Id], await pins.ListAsync(Guid.Empty));
    }

    private sealed class InMemoryPinStore : IDashboardPinStore
    {
        public List<(Guid UserId, Guid DashboardId)> Pins { get; } = [];

        public Task<IReadOnlyList<Guid>> ListAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Guid>>(Pins.Where(p => p.UserId == userId).Select(p => p.DashboardId).Reverse().ToList());

        public Task PinAsync(Guid userId, Guid dashboardId, CancellationToken cancellationToken = default)
        {
            if (!Pins.Contains((userId, dashboardId)))
            {
                Pins.Add((userId, dashboardId));
            }

            return Task.CompletedTask;
        }

        public Task UnpinAsync(Guid userId, Guid dashboardId, CancellationToken cancellationToken = default)
        {
            Pins.Remove((userId, dashboardId));
            return Task.CompletedTask;
        }
    }
}
