using Flare.Api.Alerting;
using Flare.Api.Auth;
using Flare.Identity.Auth;
using Flare.Identity.SourceLinks;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Flare.Api.Tests.Auth;

public class ProtectedIdentitySecretStoresTests
{
    private static NotificationSecretProtector Protector() => new(new EphemeralDataProtectionProvider());

    private sealed class FakeEntraStore(string? secret) : IEntraSettingsStore
    {
        public string? Stored { get; private set; } = secret;

        public Task<EntraSettings> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new EntraSettings(true, "t", "c", Stored, null));

        public Task<EntraSettings> SaveAsync(bool enabled, string? tenantId, string? clientId, string? clientSecret, CancellationToken cancellationToken = default)
        {
            Stored = clientSecret ?? Stored;
            return GetAsync(cancellationToken);
        }
    }

    private sealed class FakeSourceLinkStore : ISourceLinkStore
    {
        public List<SourceLinkConfig> Rows { get; } = [];

        public Task<IReadOnlyList<SourceLinkConfig>> GetAllAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<SourceLinkConfig>>([.. Rows]);

        public Task SetAsync(SourceLinkConfig config, CancellationToken cancellationToken = default)
        {
            Rows.RemoveAll(r => r.ServiceName == config.ServiceName);
            Rows.Add(config);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string serviceName, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task Entra_SaveStoresCiphertextAndGetReturnsPlaintext()
    {
        var inner = new FakeEntraStore(null);
        var store = new ProtectedEntraSettingsStore(inner, Protector());

        var saved = await store.SaveAsync(true, "t", "c", "s3cret");

        Assert.StartsWith(NotificationSecretProtector.Prefix, inner.Stored);
        Assert.DoesNotContain("s3cret", inner.Stored);
        Assert.Equal("s3cret", saved.ClientSecret);
        Assert.Equal("s3cret", (await store.GetAsync()).ClientSecret);
    }

    [Fact]
    public async Task Entra_NullSecretKeepsStoredValue()
    {
        var inner = new FakeEntraStore(null);
        var store = new ProtectedEntraSettingsStore(inner, Protector());
        await store.SaveAsync(true, "t", "c", "s3cret");
        var before = inner.Stored;

        await store.SaveAsync(false, "t", "c", null);

        Assert.Equal(before, inner.Stored);
    }

    [Fact]
    public async Task Entra_BackfillEncryptsLegacyPlaintextOnce()
    {
        var inner = new FakeEntraStore("legacy");
        var store = new ProtectedEntraSettingsStore(inner, Protector());

        Assert.Equal(1, await store.ProtectLegacySecretsAsync(default));
        Assert.StartsWith(NotificationSecretProtector.Prefix, inner.Stored);
        Assert.Equal(0, await store.ProtectLegacySecretsAsync(default));
        Assert.Equal("legacy", (await store.GetAsync()).ClientSecret);
    }

    [Fact]
    public async Task SourceLink_TokenEncryptedAtRestAndClearedByEmptyString()
    {
        var inner = new FakeSourceLinkStore();
        var store = new ProtectedSourceLinkStore(inner, Protector());

        await store.SetAsync(new SourceLinkConfig("svc", SourceLinkProvider.GitHub, "https://github.com/a/b", "main", "/src/", "ghp_token"));

        Assert.StartsWith(NotificationSecretProtector.Prefix, inner.Rows[0].AccessToken);
        Assert.Equal("ghp_token", (await store.GetAllAsync())[0].AccessToken);

        await store.SetAsync(inner.Rows[0] with { AccessToken = "" });
        Assert.Equal("", inner.Rows[0].AccessToken);
    }
}
