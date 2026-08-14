using System.Text;
using System.Text.Json;
using Flare.Api.Endpoints;
using Flare.Api.Json;
using Flare.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Api.Tests.Endpoints;

/// <summary>Exercises <see cref="AuthSettingsEndpoints"/>' handlers directly, same
/// "fake the interfaces, execute the real IResult" convention as
/// <see cref="EntraSettingsEndpointsTests"/>.</summary>
public class AuthSettingsEndpointsTests
{
    private static readonly IServiceProvider EmptyRequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();

    private static DefaultHttpContext CreateContext(object? jsonBody = null)
    {
        var context = new DefaultHttpContext { RequestServices = EmptyRequestServices };
        if (jsonBody is not null)
        {
            context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(jsonBody)));
        }
        context.Response.Body = new MemoryStream();
        return context;
    }

    [Fact]
    public async Task Get_ReportsCurrentSettings()
    {
        var authSettings = new FakeAuthSettingsStore(enabled: false, localEnabled: true);
        var context = CreateContext();

        var result = await AuthSettingsEndpoints.HandleGetAsync(authSettings, CancellationToken.None);
        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        var dto = await JsonSerializer.DeserializeAsync(context.Response.Body, AuthSettingsJsonContext.Default.AuthSettingsDto);
        Assert.False(dto!.Enabled);
        Assert.True(dto.LocalEnabled);
    }

    [Fact]
    public async Task Put_EnablesAuth_WhenLocalIsEnabled()
    {
        var authSettings = new FakeAuthSettingsStore(enabled: false, localEnabled: true);
        var context = CreateContext(new { enabled = true, localEnabled = true });

        var result = await AuthSettingsEndpoints.HandlePutAsync(context, authSettings, new FakeEntraSettingsStore(), new FakeLdapSettingsStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.True((await authSettings.GetAsync()).Enabled);
    }

    [Fact]
    public async Task Put_Returns400_WhenEnablingWithNoUsableMethod()
    {
        var authSettings = new FakeAuthSettingsStore(enabled: false, localEnabled: false);
        var context = CreateContext(new { enabled = true, localEnabled = false });

        var result = await AuthSettingsEndpoints.HandlePutAsync(context, authSettings, new FakeEntraSettingsStore(), new FakeLdapSettingsStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        Assert.False((await authSettings.GetAsync()).Enabled);
    }

    [Fact]
    public async Task Put_AllowsEnabling_WithOnlyEntraAsTheUsableMethod()
    {
        var authSettings = new FakeAuthSettingsStore(enabled: false, localEnabled: false);
        var entraSettings = new FakeEntraSettingsStore(new Flare.Identity.Auth.EntraSettings(true, "tenant-1", "client-1", "secret-1", DateTimeOffset.UtcNow));
        var context = CreateContext(new { enabled = true, localEnabled = false });

        var result = await AuthSettingsEndpoints.HandlePutAsync(context, authSettings, entraSettings, new FakeLdapSettingsStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Put_AllowsDisablingAuth_RegardlessOfMethods()
    {
        var authSettings = new FakeAuthSettingsStore(enabled: true, localEnabled: false);
        var context = CreateContext(new { enabled = false, localEnabled = false });

        var result = await AuthSettingsEndpoints.HandlePutAsync(context, authSettings, new FakeEntraSettingsStore(), new FakeLdapSettingsStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task Put_AllowsEnabling_WithOnlyActiveDirectoryAsTheUsableMethod()
    {
        var authSettings = new FakeAuthSettingsStore(enabled: false, localEnabled: false);
        var ldapSettings = new FakeLdapSettingsStore(new Flare.Identity.Auth.LdapSettings(
            true, "dc.corp.example.com", 636, true, "DC=corp,DC=example,DC=com", "CN=svc,DC=corp,DC=example,DC=com",
            "secret", "(&(objectClass=user)(sAMAccountName={0}))", "objectGUID", null, null, null,
            Flare.Identity.Users.UserRole.Viewer, DateTimeOffset.UtcNow));
        var context = CreateContext(new { enabled = true, localEnabled = false });

        var result = await AuthSettingsEndpoints.HandlePutAsync(context, authSettings, new FakeEntraSettingsStore(), ldapSettings, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }
}
