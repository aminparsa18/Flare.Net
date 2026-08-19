using System.Security.Cryptography.X509Certificates;
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

/// <summary>Exercises <see cref="LdapSettingsEndpoints"/>' handlers directly against
/// <see cref="FakeLdapSettingsStore"/>, same convention as
/// <see cref="EntraSettingsEndpointsTests"/>.</summary>
public class LdapSettingsEndpointsTests
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

    private static object ValidRequestBody(bool enabled = true, string? bindPassword = "secret-1", string? pinnedCertificatePem = null) => new
    {
        enabled,
        host = "dc.corp.example.com",
        port = 636,
        useSsl = true,
        baseDn = "DC=corp,DC=example,DC=com",
        bindDn = "CN=flare-svc,DC=corp,DC=example,DC=com",
        bindPassword,
        userSearchFilter = "(&(objectClass=user)(sAMAccountName={0}))",
        uniqueIdAttribute = "objectGUID",
        adminGroupDn = (string?)null,
        memberGroupDn = (string?)null,
        viewerGroupDn = (string?)null,
        defaultRole = "Viewer",
        pinnedCertificatePem,
    };

    [Fact]
    public async Task Get_ReportsNotConfigured_WhenNoSettingsSavedYet()
    {
        var context = CreateContext();

        var result = await LdapSettingsEndpoints.HandleGetAsync(new FakeLdapSettingsStore(), CancellationToken.None);
        await result.ExecuteAsync(context);

        var dto = await ReadJsonBodyAsync(context, LdapSettingsJsonContext.Default.LdapSettingsDto);
        Assert.False(dto!.Enabled);
        Assert.False(dto.HasBindPassword);
        Assert.Equal(636, dto.Port);
        Assert.Equal("objectGUID", dto.UniqueIdAttribute);
    }

    [Fact]
    public async Task Get_NeverReturnsTheRealBindPassword()
    {
        var store = new FakeLdapSettingsStore(new LdapSettings(
            true, "dc.corp.example.com", 636, true, "DC=corp,DC=example,DC=com", "CN=svc,DC=corp,DC=example,DC=com",
            "super-secret-value", "(&(objectClass=user)(sAMAccountName={0}))", "objectGUID", null, null, null,
            UserRole.Viewer, DateTimeOffset.UtcNow));
        var context = CreateContext();

        var result = await LdapSettingsEndpoints.HandleGetAsync(store, CancellationToken.None);
        await result.ExecuteAsync(context);

        context.Response.Body.Position = 0;
        var raw = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("super-secret-value", raw);

        var dto = await ReadJsonBodyAsync(context, LdapSettingsJsonContext.Default.LdapSettingsDto);
        Assert.True(dto!.HasBindPassword);
    }

    [Fact]
    public async Task Put_PersistsHostAndBaseDn()
    {
        var store = new FakeLdapSettingsStore();
        var context = CreateContext(ValidRequestBody());

        var result = await LdapSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        var saved = await store.GetAsync();
        Assert.True(saved.Enabled);
        Assert.Equal("dc.corp.example.com", saved.Host);
        Assert.Equal("DC=corp,DC=example,DC=com", saved.BaseDn);
        Assert.Equal("secret-1", saved.BindPassword);
    }

    [Fact]
    public async Task Put_WithNullBindPassword_PreservesTheExistingOne()
    {
        var store = new FakeLdapSettingsStore(new LdapSettings(
            true, "dc.corp.example.com", 636, true, "DC=corp,DC=example,DC=com", "CN=svc,DC=corp,DC=example,DC=com",
            "original-secret", "(&(objectClass=user)(sAMAccountName={0}))", "objectGUID", null, null, null,
            UserRole.Viewer, DateTimeOffset.UtcNow));
        var context = CreateContext(ValidRequestBody(bindPassword: null));

        var result = await LdapSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal("original-secret", (await store.GetAsync()).BindPassword);
    }

    [Fact]
    public async Task Put_Returns400_WhenEnablingWithoutABaseDn()
    {
        var store = new FakeLdapSettingsStore();
        var body = new
        {
            enabled = true,
            host = "dc.corp.example.com",
            port = 636,
            useSsl = true,
            baseDn = (string?)null,
            bindDn = "CN=flare-svc,DC=corp,DC=example,DC=com",
            bindPassword = "secret-1",
            userSearchFilter = "(&(objectClass=user)(sAMAccountName={0}))",
            uniqueIdAttribute = "objectGUID",
            adminGroupDn = (string?)null,
            memberGroupDn = (string?)null,
            viewerGroupDn = (string?)null,
            defaultRole = "Viewer",
        };
        var context = CreateContext(body);

        var result = await LdapSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Put_Returns400_WhenEnablingWithNoBindPasswordEverSaved()
    {
        var store = new FakeLdapSettingsStore();
        var context = CreateContext(ValidRequestBody(bindPassword: null));

        var result = await LdapSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Put_AllowsDisabling_WithoutSupplyingCredentials()
    {
        var store = new FakeLdapSettingsStore(new LdapSettings(
            true, "dc.corp.example.com", 636, true, "DC=corp,DC=example,DC=com", "CN=svc,DC=corp,DC=example,DC=com",
            "secret-1", "(&(objectClass=user)(sAMAccountName={0}))", "objectGUID", null, null, null,
            UserRole.Viewer, DateTimeOffset.UtcNow));
        var context = CreateContext(ValidRequestBody(enabled: false, bindPassword: null));

        var result = await LdapSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.False((await store.GetAsync()).Enabled);
    }

    [Fact]
    public async Task Put_Returns400_WhenPinnedCertificatePemIsNotValidPem()
    {
        var store = new FakeLdapSettingsStore();
        var context = CreateContext(ValidRequestBody(pinnedCertificatePem: "not a certificate"));

        var result = await LdapSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Put_PersistsAValidPinnedCertificatePem()
    {
        var store = new FakeLdapSettingsStore();
        var pem = TestCertificates.CreateSelfSigned().ExportCertificatePem();
        var context = CreateContext(ValidRequestBody(pinnedCertificatePem: pem));

        var result = await LdapSettingsEndpoints.HandlePutAsync(context, store, CancellationToken.None);
        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        Assert.Equal(pem, (await store.GetAsync()).PinnedCertificatePem);
    }

    [Fact]
    public async Task Get_ReturnsThePinnedCertificatePemInFull()
    {
        // Contrast with Get_NeverReturnsTheRealBindPassword above - a certificate isn't a
        // secret, so unlike BindPassword/HasBindPassword there's no redaction here.
        var pem = TestCertificates.CreateSelfSigned().ExportCertificatePem();
        var store = new FakeLdapSettingsStore(new LdapSettings(
            true, "dc.corp.example.com", 636, true, "DC=corp,DC=example,DC=com", "CN=svc,DC=corp,DC=example,DC=com",
            "secret", "(&(objectClass=user)(sAMAccountName={0}))", "objectGUID", null, null, null,
            UserRole.Viewer, DateTimeOffset.UtcNow, PinnedCertificatePem: pem));
        var context = CreateContext();

        var result = await LdapSettingsEndpoints.HandleGetAsync(store, CancellationToken.None);
        await result.ExecuteAsync(context);

        var dto = await ReadJsonBodyAsync(context, LdapSettingsJsonContext.Default.LdapSettingsDto);
        Assert.Equal(pem, dto!.PinnedCertificatePem);
    }
}
