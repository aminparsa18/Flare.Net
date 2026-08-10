using System.Text;
using System.Text.Json;
using Flare.Api.Endpoints;
using Flare.Api.Json;
using Flare.Api.Tests.TestSupport;
using Flare.Identity.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Api.Tests.Endpoints;

/// <summary>
/// Exercises <see cref="EntraSettingsEndpoints"/>' handlers directly against
/// <see cref="FakeEntraSettingsStore"/>, same convention as <see cref="AuthEndpointsTests"/>.
/// </summary>
public class EntraSettingsEndpointsTests
{
    private static readonly IServiceProvider EmptyRequestServices = new ServiceCollection().AddLogging().BuildServiceProvider();

    private static DefaultHttpContext CreateContext(object? jsonBody = null)
    {
        var context = new DefaultHttpContext { RequestServices = EmptyRequestServices };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("flare.example.com");
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

    [Fact]
    public async Task Get_ReportsNotConfigured_WhenNoSettingsSavedYet()
    {
        var context = CreateContext();

        var result = await EntraSettingsEndpoints.HandleGetAsync(context, new FakeEntraSettingsStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        var dto = await ReadJsonBodyAsync(context, EntraSettingsJsonContext.Default.EntraSettingsDto);
        Assert.False(dto!.Enabled);
        Assert.False(dto.HasClientSecret);
        Assert.Equal("https://flare.example.com/signin-oidc", dto.RedirectUri);
    }

    [Fact]
    public async Task Get_NeverReturnsTheRealClientSecret()
    {
        var store = new FakeEntraSettingsStore(new EntraSettings(true, "tenant-1", "client-1", "super-secret-value", DateTimeOffset.UtcNow));
        var context = CreateContext();

        var result = await EntraSettingsEndpoints.HandleGetAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        var raw = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("super-secret-value", raw);

        var dto = await ReadJsonBodyAsync(context, EntraSettingsJsonContext.Default.EntraSettingsDto);
        Assert.True(dto!.HasClientSecret);
    }

    [Fact]
    public async Task Put_PersistsTenantAndClientId()
    {
        var store = new FakeEntraSettingsStore();
        var context = CreateContext(new { enabled = true, tenantId = "tenant-1", clientId = "client-1", clientSecret = "secret-1" });

        var result = await EntraSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        var saved = await store.GetAsync();
        Assert.True(saved.Enabled);
        Assert.Equal("tenant-1", saved.TenantId);
        Assert.Equal("client-1", saved.ClientId);
        Assert.Equal("secret-1", saved.ClientSecret);
    }

    [Fact]
    public async Task Put_WithNullClientSecret_PreservesTheExistingOne()
    {
        var store = new FakeEntraSettingsStore(new EntraSettings(true, "tenant-1", "client-1", "original-secret", DateTimeOffset.UtcNow));
        var context = CreateContext(new { enabled = true, tenantId = "tenant-2", clientId = "client-1", clientSecret = (string?)null });

        var result = await EntraSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        var saved = await store.GetAsync();
        Assert.Equal("tenant-2", saved.TenantId);
        Assert.Equal("original-secret", saved.ClientSecret);
    }

    [Fact]
    public async Task Put_Returns400_WhenEnablingWithoutATenantId()
    {
        var store = new FakeEntraSettingsStore();
        var context = CreateContext(new { enabled = true, tenantId = (string?)null, clientId = "client-1", clientSecret = "secret-1" });

        var result = await EntraSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Put_Returns400_WhenEnablingWithNoClientSecretEverSaved()
    {
        var store = new FakeEntraSettingsStore();
        var context = CreateContext(new { enabled = true, tenantId = "tenant-1", clientId = "client-1", clientSecret = (string?)null });

        var result = await EntraSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Put_AllowsDisabling_WithoutSupplyingCredentials()
    {
        var store = new FakeEntraSettingsStore(new EntraSettings(true, "tenant-1", "client-1", "secret-1", DateTimeOffset.UtcNow));
        var context = CreateContext(new { enabled = false, tenantId = "tenant-1", clientId = "client-1", clientSecret = (string?)null });

        var result = await EntraSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.False((await store.GetAsync()).Enabled);
    }
}
