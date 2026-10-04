using Flare.Identity;
using Flare.Identity.Apdex;
using Flare.Identity.Audit;
using Flare.Identity.Auth;
using Flare.Identity.DashboardPins;
using Flare.Identity.UserPreferences;
using Flare.Identity.IngestKeys;
using Flare.Identity.LlmPrices;
using Flare.Identity.MetricMetadata;
using Flare.Identity.PasswordSetTokens;
using Flare.Identity.PersonalAccessTokens;
using Flare.Identity.SourceLinks;
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
        builder.Services.AddSingleton<IUserStore, DbUserStore>();
        builder.Services.AddSingleton<ISessionStore, DbSessionStore>();
        builder.Services.AddSingleton<IPasswordSetTokenStore, DbPasswordSetTokenStore>();
        builder.Services.AddSingleton<ILoginAttemptStore, DbLoginAttemptStore>();
        builder.Services.AddSingleton<IIngestApiKeyStore, DbIngestApiKeyStore>();
        builder.Services.AddSingleton<IPersonalAccessTokenStore, DbPersonalAccessTokenStore>();
        builder.Services.AddSingleton<IEntraSettingsStore, DbEntraSettingsStore>();
        builder.Services.AddSingleton<IAuthSettingsStore, DbAuthSettingsStore>();
        builder.Services.AddSingleton<ILdapSettingsStore, DbLdapSettingsStore>();
        builder.Services.AddSingleton<IOidcSettingsStore, DbOidcSettingsStore>();
        builder.Services.AddSingleton<IProxyAuthSettingsStore, DbProxyAuthSettingsStore>();
        builder.Services.AddSingleton<IApdexThresholdStore, DbApdexThresholdStore>();
        builder.Services.AddSingleton<ISourceLinkStore, DbSourceLinkStore>();
        builder.Services.AddSingleton<IMetricMetadataOverrideStore, DbMetricMetadataOverrideStore>();
        builder.Services.AddSingleton<ILlmModelPriceStore, DbLlmModelPriceStore>();
        builder.Services.AddSingleton<IDashboardPinStore, DbDashboardPinStore>();
        builder.Services.AddSingleton<IUserPreferencesStore, DbUserPreferencesStore>();
        builder.Services.AddSingleton<IAuditEventStore, DbAuditEventStore>();
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
        builder.Services.AddSingleton<IIngestApiKeyStore, DbIngestApiKeyStore>();
        return builder;
    }
}
