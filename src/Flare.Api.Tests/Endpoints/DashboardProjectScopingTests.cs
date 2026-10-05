using System.Security.Claims;
using System.Text.Json;
using Flare.Api.Auth;
using Flare.Api.Endpoints;
using Flare.Api.Model;
using Flare.Api.Tests.TestSupport;
using Flare.Identity.Projects;
using Flare.Identity.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Api.Tests.Endpoints;

/// <summary>Project ownership of dashboards (ADR-0123 phase 3), same direct-handler convention as <see cref="DashboardEndpointsTests"/>.</summary>
public class DashboardProjectScopingTests
{
    private static readonly IServiceProvider EmptyRequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();

    private readonly FakeDashboardQueryService _dashboards = new();
    private readonly FakeProjectStore _projects = new();
    private readonly Project _payments;

    public DashboardProjectScopingTests() => _payments = _projects.Seed("payments");

    private static DefaultHttpContext CreateContext(Guid userId, UserRole globalRole, UserRole? projectRole, Guid? projectId, object? body = null)
    {
        var context = new DefaultHttpContext { RequestServices = EmptyRequestServices };
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim(ClaimTypes.Role, globalRole.ToString())],
            authenticationType: "FlareSession"));
        // Mirrors ProjectScopeMiddleware: a global Admin is unrestricted, everyone else gets their memberships.
        context.SetProjectAccess(globalRole == UserRole.Admin
            ? ProjectAccess.Unrestricted
            : ProjectAccess.ForMember(projectRole is { } role && projectId is { } id ? [new ProjectMembership(id, role)] : []));
        if (body is not null)
        {
            context.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(body)));
        }

        context.Response.Body = new MemoryStream();
        return context;
    }

    private Dashboard Seed(Guid? projectId, Guid? owner = null)
    {
        var dashboard = new Dashboard
        {
            Id = Guid.NewGuid(),
            Name = "Existing",
            OwnerUserId = owner,
            ProjectId = projectId,
            LayoutJson = JsonDocument.Parse("{}").RootElement,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        _dashboards.Seed(dashboard);
        return dashboard;
    }

    // ForbidHttpResult needs the authentication service to execute, so map it to its status code directly.
    private static async Task<int> StatusAsync(IResult result, HttpContext context)
    {
        if (result is Microsoft.AspNetCore.Http.HttpResults.ForbidHttpResult)
        {
            return StatusCodes.Status403Forbidden;
        }

        await result.ExecuteAsync(context);
        return context.Response.StatusCode;
    }

    [Fact]
    public async Task Create_InAProjectTheCallerIsNotIn_IsForbidden()
    {
        var context = CreateContext(Guid.NewGuid(), UserRole.Member, null, null, new { name = "n", layoutJson = new { }, projectId = _payments.Id });

        var status = await StatusAsync(await DashboardEndpoints.HandleCreateAsync(context, context.User, _dashboards, _projects, default), context);

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.Empty(await _dashboards.ListAsync(default));
    }

    [Fact]
    public async Task Create_InAProjectAsMember_StoresTheProject()
    {
        var context = CreateContext(Guid.NewGuid(), UserRole.Member, UserRole.Member, _payments.Id, new { name = "n", layoutJson = new { }, projectId = _payments.Id });

        var status = await StatusAsync(await DashboardEndpoints.HandleCreateAsync(context, context.User, _dashboards, _projects, default), context);

        Assert.Equal(StatusCodes.Status201Created, status);
        Assert.Equal(_payments.Id, (await _dashboards.ListAsync(default)).Single().ProjectId);
    }

    [Fact]
    public async Task Create_InAnUnknownProject_Is400()
    {
        var context = CreateContext(Guid.NewGuid(), UserRole.Admin, null, null, new { name = "n", layoutJson = new { }, projectId = Guid.NewGuid() });

        var status = await StatusAsync(await DashboardEndpoints.HandleCreateAsync(context, context.User, _dashboards, _projects, default), context);

        Assert.Equal(StatusCodes.Status400BadRequest, status);
    }

    [Fact]
    public async Task Create_ByAProjectViewer_IsForbidden()
    {
        var context = CreateContext(Guid.NewGuid(), UserRole.Member, UserRole.Viewer, _payments.Id, new { name = "n", layoutJson = new { }, projectId = _payments.Id });

        var status = await StatusAsync(await DashboardEndpoints.HandleCreateAsync(context, context.User, _dashboards, _projects, default), context);

        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task List_HidesProjectDashboards_FromNonMembers()
    {
        Seed(null);
        Seed(_payments.Id);
        var context = CreateContext(Guid.NewGuid(), UserRole.Member, null, null);

        await (await DashboardEndpoints.HandleListAsync(context, _dashboards, default)).ExecuteAsync(context);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();

        Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(body, "\"id\"", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count);
    }

    [Fact]
    public async Task Get_OfAProjectDashboard_Is404ForNonMembers_AndOkForMembers()
    {
        var dashboard = Seed(_payments.Id);
        var outsider = CreateContext(Guid.NewGuid(), UserRole.Member, null, null);
        var member = CreateContext(Guid.NewGuid(), UserRole.Viewer, UserRole.Viewer, _payments.Id);

        Assert.Equal(StatusCodes.Status404NotFound, await StatusAsync(await DashboardEndpoints.HandleGetAsync(dashboard.Id, outsider, _dashboards, default), outsider));
        Assert.Equal(StatusCodes.Status200OK, await StatusAsync(await DashboardEndpoints.HandleGetAsync(dashboard.Id, member, _dashboards, default), member));
    }

    [Fact]
    public async Task Update_ByNonMember_Is404_ByProjectViewer_Is403()
    {
        var dashboard = Seed(_payments.Id);
        var body = new { name = "x", layoutJson = new { } };
        var outsider = CreateContext(Guid.NewGuid(), UserRole.Member, null, null, body);
        var viewer = CreateContext(Guid.NewGuid(), UserRole.Member, UserRole.Viewer, _payments.Id, body);

        Assert.Equal(StatusCodes.Status404NotFound, await StatusAsync(await DashboardEndpoints.HandleUpdateAsync(dashboard.Id, outsider, outsider.User, _dashboards, _projects, default), outsider));
        Assert.Equal(StatusCodes.Status403Forbidden, await StatusAsync(await DashboardEndpoints.HandleUpdateAsync(dashboard.Id, viewer, viewer.User, _dashboards, _projects, default), viewer));
    }

    [Fact]
    public async Task Update_ProjectMemberCannotEditSomeoneElsesDashboard_ButProjectAdminCan()
    {
        var dashboard = Seed(_payments.Id, owner: Guid.NewGuid());
        var body = new { name = "x", layoutJson = new { } };
        var member = CreateContext(Guid.NewGuid(), UserRole.Member, UserRole.Member, _payments.Id, body);
        var admin = CreateContext(Guid.NewGuid(), UserRole.Member, UserRole.Admin, _payments.Id, body);

        Assert.Equal(StatusCodes.Status403Forbidden, await StatusAsync(await DashboardEndpoints.HandleUpdateAsync(dashboard.Id, member, member.User, _dashboards, _projects, default), member));
        Assert.Equal(StatusCodes.Status200OK, await StatusAsync(await DashboardEndpoints.HandleUpdateAsync(dashboard.Id, admin, admin.User, _dashboards, _projects, default), admin));
    }

    [Fact]
    public async Task Update_WithoutProjectId_KeepsTheProject_AndEmptyGuidClearsIt()
    {
        var dashboard = Seed(_payments.Id);
        var keep = CreateContext(Guid.NewGuid(), UserRole.Admin, null, null, new { name = "x", layoutJson = new { } });
        await DashboardEndpoints.HandleUpdateAsync(dashboard.Id, keep, keep.User, _dashboards, _projects, default);
        Assert.Equal(_payments.Id, (await _dashboards.GetAsync(dashboard.Id, default))!.ProjectId);

        var clear = CreateContext(Guid.NewGuid(), UserRole.Admin, null, null, new { name = "x", layoutJson = new { }, projectId = Guid.Empty });
        await DashboardEndpoints.HandleUpdateAsync(dashboard.Id, clear, clear.User, _dashboards, _projects, default);
        Assert.Null((await _dashboards.GetAsync(dashboard.Id, default))!.ProjectId);
    }

    [Fact]
    public async Task Update_MovingIntoAProjectTheCallerCannotWrite_IsForbidden()
    {
        var other = _projects.Seed("search");
        var dashboard = Seed(_payments.Id);
        var context = CreateContext(Guid.NewGuid(), UserRole.Member, UserRole.Admin, _payments.Id, new { name = "x", layoutJson = new { }, projectId = other.Id });

        var status = await StatusAsync(await DashboardEndpoints.HandleUpdateAsync(dashboard.Id, context, context.User, _dashboards, _projects, default), context);

        Assert.Equal(StatusCodes.Status403Forbidden, status);
        Assert.Equal(_payments.Id, (await _dashboards.GetAsync(dashboard.Id, default))!.ProjectId);
    }

    [Fact]
    public async Task Delete_ByNonMember_Is404()
    {
        var dashboard = Seed(_payments.Id);
        var context = CreateContext(Guid.NewGuid(), UserRole.Member, null, null);

        var status = await StatusAsync(await DashboardEndpoints.HandleDeleteAsync(dashboard.Id, context, context.User, _dashboards, default), context);

        Assert.Equal(StatusCodes.Status404NotFound, status);
        Assert.NotNull(await _dashboards.GetAsync(dashboard.Id, default));
    }
}
