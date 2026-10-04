using System.Text.Json;
using Flare.Api.Endpoints;
using Flare.Api.Tests.TestSupport;
using Flare.Identity.Users;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Flare.Api.Tests.Endpoints;

public class ForgotPasswordEndpointTests
{
    private static readonly IServiceProvider Services = new ServiceCollection().AddLogging().BuildServiceProvider();

    private static async Task<int> Post(string username, FakeUserStore users, FakePasswordSetTokenStore tokens, FakePasswordResetMailer mailer)
    {
        var c = new DefaultHttpContext { RequestServices = Services };
        c.Request.Body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { username })));
        c.Response.Body = new MemoryStream();
        var r = await AuthEndpoints.HandleForgotPasswordAsync(c, users, tokens, new FakeAuthSettingsStore(), mailer, default);
        await r.ExecuteAsync(c);
        return c.Response.StatusCode;
    }

    [Fact]
    public async Task EmailUsername_GetsALinkThatSetsThePassword()
    {
        var users = new FakeUserStore();
        var tokens = new FakePasswordSetTokenStore();
        var mailer = new FakePasswordResetMailer();
        var u = await users.CreateAsync("ada@example.com", "password-123", UserRole.Viewer);

        Assert.Equal(204, await Post("ada@example.com", users, tokens, mailer));

        var sent = Assert.Single(mailer.Sent);
        Assert.Equal("ada@example.com", sent.To);
        var raw = sent.Link[(sent.Link.IndexOf("token=", StringComparison.Ordinal) + 6)..];
        Assert.Equal(u.Id, await tokens.ConsumeAsync(raw));
    }

    [Fact]
    public async Task UnknownUser_And_NonEmailUsername_Return204WithoutSending()
    {
        var users = new FakeUserStore();
        var tokens = new FakePasswordSetTokenStore();
        var mailer = new FakePasswordResetMailer();
        await users.CreateAsync("plainname", "password-123", UserRole.Viewer);

        Assert.Equal(204, await Post("ghost@example.com", users, tokens, mailer));
        Assert.Equal(204, await Post("plainname", users, tokens, mailer));
        Assert.Empty(mailer.Sent);
    }

    [Fact]
    public async Task SecondRequestWithinCooldown_DoesNotSendAgain()
    {
        var users = new FakeUserStore();
        var mailer = new FakePasswordResetMailer();
        await users.CreateAsync("grace@example.com", "password-123", UserRole.Viewer);

        var tokens = new FakePasswordSetTokenStore();
        await Post("grace@example.com", users, tokens, mailer);
        Assert.Equal(204, await Post("GRACE@example.com", users, tokens, mailer));
        Assert.Single(mailer.Sent);
    }

    [Fact]
    public async Task NotConfigured_Returns404()
    {
        var mailer = new FakePasswordResetMailer { IsConfigured = false };
        Assert.Equal(404, await Post("x@example.com", new FakeUserStore(), new FakePasswordSetTokenStore(), mailer));
    }
}
