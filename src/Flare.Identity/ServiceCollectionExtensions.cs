using Flare.Identity;
using Flare.Identity.Auth;
using Flare.Identity.IngestKeys;
using Flare.Identity.Users;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Extensions.Hosting;

// Same "TBuilder : IHostApplicationBuilder" extension shape as
// Flare.ServiceDefaults.Extensions.AddServiceDefaults, and the same namespace
// (Microsoft.Extensions.Hosting) so builder.AddFlareIdentity()/AddFlareIngestAuth() are
// available without an extra using, matching how AddServiceDefaults() already reads.
public static class FlareIdentityServiceCollectionExtensions
{
    /// <summary>Full identity wiring for <c>Flare.Api</c>: stores for users, sessions,
    /// and ingest API keys, plus the password hasher. Does NOT register the
    /// authentication scheme/authorization policies themselves - those live in
    /// <c>Flare.Api/Program.cs</c> (alongside <c>AddAuthentication</c>/
    /// <c>AddAuthorizationBuilder</c>) since they also need endpoint-routing types this
    /// project deliberately doesn't take a hard dependency on beyond the
    /// FrameworkReference already needed for <see cref="SessionAuthenticationHandler"/>.</summary>
    public static TBuilder AddFlareIdentity<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.Configure<IdentityOptions>(builder.Configuration.GetSection(IdentityOptions.SectionName));
        builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
        builder.Services.Configure<EntraOptions>(builder.Configuration.GetSection(EntraOptions.SectionName));
        builder.Services.AddSingleton<IdentityDbConnectionFactory>();
        builder.Services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        builder.Services.AddSingleton<IUserStore, SqliteUserStore>();
        builder.Services.AddSingleton<ISessionStore, SqliteSessionStore>();
        builder.Services.AddSingleton<IIngestApiKeyStore, SqliteIngestApiKeyStore>();
        builder.Services.AddSingleton<IEntraSettingsStore, SqliteEntraSettingsStore>();
        builder.Services.AddSingleton<IAuthSettingsStore, SqliteAuthSettingsStore>();
        builder.Services.AddSingleton<ILdapSettingsStore, SqliteLdapSettingsStore>();
        return builder;
    }

    /// <summary>Narrow wiring for <c>Flare.Ingest</c>: only what's needed to validate
    /// OTLP ingest API keys against the same SQLite file - no <see cref="IUserStore"/>,
    /// no <see cref="ISessionStore"/>, no <see cref="AspNetPasswordHasher"/>. Ingest never
    /// authenticates a person, only a machine's API key (see IngestKeys/ remarks).</summary>
    public static TBuilder AddFlareIngestAuth<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        builder.Services.Configure<IdentityOptions>(builder.Configuration.GetSection(IdentityOptions.SectionName));
        builder.Services.AddSingleton<IdentityDbConnectionFactory>();
        builder.Services.AddSingleton<IIngestApiKeyStore, SqliteIngestApiKeyStore>();
        return builder;
    }
}
