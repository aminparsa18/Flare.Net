using System.Text;
using System.Text.Json;
using Flare.Api.Endpoints;
using Flare.Api.Json;
using Flare.Api.Tests.TestSupport;
using Flare.Identity.Auth;
using Flare.Identity.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Api.Tests.Endpoints;

/// <summary>Exercises <see cref="ProxyAuthSettingsEndpoints"/>' handlers directly against
/// <see cref="FakeProxyAuthSettingsStore"/>, same convention as
/// <see cref="LdapSettingsEndpointsTests"/>.</summary>
public class ProxyAuthSettingsEndpointsTests
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

    private static async Task<T?> ReadJsonBodyAsync<T>(DefaultHttpContext context, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        context.Response.Body.Position = 0;
        return await JsonSerializer.DeserializeAsync(context.Response.Body, typeInfo);
    }

    private static object ValidRequestBody(bool enabled = true, string trustedProxyCidrs = "172.18.0.0/16") => new
    {
        enabled,
        headerName = "Remote-User",
        trustedProxyCidrs,
        groupsHeaderName = (string?)null,
        adminGroup = (string?)null,
        memberGroup = (string?)null,
        viewerGroup = (string?)null,
        defaultRole = "Viewer",
    };

    [Fact]
    public async Task Get_ReportsNotConfigured_WhenNoSettingsSavedYet()
    {
        var context = CreateContext();

        var result = await ProxyAuthSettingsEndpoints.HandleGetAsync(new FakeProxyAuthSettingsStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        var dto = await ReadJsonBodyAsync(context, ProxyAuthJsonContext.Default.ProxyAuthSettingsDto);
        Assert.False(dto!.Enabled);
        Assert.Equal("Remote-User", dto.HeaderName);
    }

    [Fact]
    public async Task Put_PersistsHeaderNameAndCidrs()
    {
        var store = new FakeProxyAuthSettingsStore();
        var context = CreateContext(ValidRequestBody());

        var result = await ProxyAuthSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        var saved = await store.GetAsync();
        Assert.True(saved.Enabled);
        Assert.Equal("Remote-User", saved.HeaderName);
        Assert.Equal("172.18.0.0/16", saved.TrustedProxyCidrs);
    }

    [Fact]
    public async Task Put_Returns400_WhenHeaderNameIsBlank()
    {
        var store = new FakeProxyAuthSettingsStore();
        var body = new
        {
            enabled = false,
            headerName = "   ",
            trustedProxyCidrs = "172.18.0.0/16",
            groupsHeaderName = (string?)null,
            adminGroup = (string?)null,
            memberGroup = (string?)null,
            viewerGroup = (string?)null,
            defaultRole = "Viewer",
        };
        var context = CreateContext(body);

        var result = await ProxyAuthSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Put_Returns400_WhenEnablingWithNoTrustedCidrs()
    {
        var store = new FakeProxyAuthSettingsStore();
        var context = CreateContext(ValidRequestBody(trustedProxyCidrs: ""));

        var result = await ProxyAuthSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Put_Returns400_WhenEnablingWithOnlyMalformedCidrs()
    {
        var store = new FakeProxyAuthSettingsStore();
        var context = CreateContext(ValidRequestBody(trustedProxyCidrs: "not-a-cidr, also-bad"));

        var result = await ProxyAuthSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Put_AllowsDisabling_WithNoTrustedCidrsConfigured()
    {
        var store = new FakeProxyAuthSettingsStore(new ProxyAuthSettings(
            true, "Remote-User", "172.18.0.0/16", null, null, null, null, UserRole.Viewer, DateTimeOffset.UtcNow));
        var context = CreateContext(ValidRequestBody(enabled: false, trustedProxyCidrs: ""));

        var result = await ProxyAuthSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.False((await store.GetAsync()).Enabled);
    }

    [Fact]
    public async Task Put_PersistsOptionalGroupsFields()
    {
        var store = new FakeProxyAuthSettingsStore();
        var body = new
        {
            enabled = true,
            headerName = "Remote-User",
            trustedProxyCidrs = "172.18.0.0/16",
            groupsHeaderName = "X-Forwarded-Groups",
            adminGroup = "admins",
            memberGroup = "members",
            viewerGroup = "viewers",
            defaultRole = "Viewer",
        };
        var context = CreateContext(body);

        var result = await ProxyAuthSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        var saved = await store.GetAsync();
        Assert.Equal("X-Forwarded-Groups", saved.GroupsHeaderName);
        Assert.Equal("admins", saved.AdminGroup);
        Assert.Equal("members", saved.MemberGroup);
        Assert.Equal("viewers", saved.ViewerGroup);
    }

    [Fact]
    public async Task Put_PersistsTheLogoutRedirectUrl()
    {
        var store = new FakeProxyAuthSettingsStore();
        var body = new
        {
            enabled = true,
            headerName = "Remote-User",
            trustedProxyCidrs = "172.18.0.0/16",
            groupsHeaderName = (string?)null,
            adminGroup = (string?)null,
            memberGroup = (string?)null,
            viewerGroup = (string?)null,
            defaultRole = "Viewer",
            logoutRedirectUrl = "https://proxy.example.com/oauth2/sign_out",
        };
        var context = CreateContext(body);

        var result = await ProxyAuthSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("https://proxy.example.com/oauth2/sign_out", (await store.GetAsync()).LogoutRedirectUrl);
    }

    [Fact]
    public async Task Put_Returns400_WhenTheLogoutRedirectUrlIsNotAValidAbsoluteUrl()
    {
        var store = new FakeProxyAuthSettingsStore();
        var body = new
        {
            enabled = true,
            headerName = "Remote-User",
            trustedProxyCidrs = "172.18.0.0/16",
            groupsHeaderName = (string?)null,
            adminGroup = (string?)null,
            memberGroup = (string?)null,
            viewerGroup = (string?)null,
            defaultRole = "Viewer",
            logoutRedirectUrl = "not-a-url",
        };
        var context = CreateContext(body);

        var result = await ProxyAuthSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }
}
