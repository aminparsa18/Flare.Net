using System.Text.Json;
using Flare.Api.Endpoints;
using Flare.Api.Json;
using Flare.Api.Tests.TestSupport;
using Flare.Identity.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Api.Tests.Endpoints;

/// <summary>
/// Exercises <see cref="UserEndpoints"/>' handlers directly against
/// <see cref="FakeUserStore"/>, same "fake the one interface, execute the real IResult"
/// convention as <see cref="AuthEndpointsTests"/>. Most of the interesting behavior here
/// is the last-enabled-Admin guard - everything else is a thin wrapper over
/// <see cref="IUserStore"/> methods that already have their own store-level tests.
/// </summary>
public class UserEndpointsTests
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

    [Fact]
    public async Task ListUsers_ReturnsEveryUser()
    {
        var users = new FakeUserStore();
        await users.CreateAsync("alice", "correctpassword1", UserRole.Admin);
        await users.CreateAsync("bob", "correctpassword2", UserRole.Viewer);
        var context = CreateContext();

        var result = await UserEndpoints.HandleListAsync(context, users, CancellationToken.None);
        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        var dto = await JsonSerializer.DeserializeAsync(context.Response.Body, UsersJsonContext.Default.UserListResponse);
        Assert.Equal(2, dto!.Users.Count);
    }

    [Fact]
    public async Task SetRole_PromotesAViewerToMember()
    {
        var users = new FakeUserStore();
        await users.CreateAsync("admin", "correctpassword1", UserRole.Admin); // keeps the guard from tripping
        var target = await users.CreateAsync("viewer", "correctpassword2", UserRole.Viewer);
        var context = CreateContext(new { role = "Member" });

        var result = await UserEndpoints.HandleSetRoleAsync(target.Id, context, users, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(UserRole.Member, (await users.FindByIdAsync(target.Id))!.Role);
    }

    [Fact]
    public async Task SetRole_Returns400_WhenDemotingTheLastEnabledAdmin()
    {
        var users = new FakeUserStore();
        var admin = await users.CreateAsync("only-admin", "correctpassword1", UserRole.Admin);
        var context = CreateContext(new { role = "Member" });

        var result = await UserEndpoints.HandleSetRoleAsync(admin.Id, context, users, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.Equal(UserRole.Admin, (await users.FindByIdAsync(admin.Id))!.Role);
    }

    [Fact]
    public async Task SetRole_AllowsDemotion_WhenAnotherEnabledAdminRemains()
    {
        var users = new FakeUserStore();
        var admin1 = await users.CreateAsync("admin-one", "correctpassword1", UserRole.Admin);
        await users.CreateAsync("admin-two", "correctpassword2", UserRole.Admin);
        var context = CreateContext(new { role = "Viewer" });

        var result = await UserEndpoints.HandleSetRoleAsync(admin1.Id, context, users, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task SetRole_Returns404_ForAnUnknownUser()
    {
        var users = new FakeUserStore();
        var context = CreateContext(new { role = "Admin" });

        var result = await UserEndpoints.HandleSetRoleAsync(Guid.NewGuid(), context, users, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
    }

    [Fact]
    public async Task SetDisabled_Returns400_WhenDisablingTheLastEnabledAdmin()
    {
        var users = new FakeUserStore();
        var admin = await users.CreateAsync("only-admin", "correctpassword1", UserRole.Admin);
        var context = CreateContext(new { isDisabled = true });

        var result = await UserEndpoints.HandleSetDisabledAsync(admin.Id, context, users, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.False((await users.FindByIdAsync(admin.Id))!.IsDisabled);
    }

    [Fact]
    public async Task SetDisabled_DisablesANonAdminUser()
    {
        var users = new FakeUserStore();
        await users.CreateAsync("admin", "correctpassword1", UserRole.Admin);
        var viewer = await users.CreateAsync("viewer", "correctpassword2", UserRole.Viewer);
        var context = CreateContext(new { isDisabled = true });

        var result = await UserEndpoints.HandleSetDisabledAsync(viewer.Id, context, users, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.True((await users.FindByIdAsync(viewer.Id))!.IsDisabled);
    }

    [Fact]
    public async Task SetDisabled_AllowsReEnabling_EvenIfItWouldBeTheOnlyAdmin()
    {
        // Re-enabling (isDisabled: false) never trips the guard - only the transition
        // *into* disabled/non-Admin does.
        var users = new FakeUserStore();
        var admin = await users.CreateAsync("only-admin", "correctpassword1", UserRole.Admin);
        await users.SetDisabledAsync(admin.Id, true);
        var context = CreateContext(new { isDisabled = false });

        var result = await UserEndpoints.HandleSetDisabledAsync(admin.Id, context, users, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }
}
