using ClickHouse.Driver;
using Flare.Api.Alerting;
using Flare.Api.Auth;
using Flare.Api.Endpoints;
using Flare.Api.LiveTail;
using Flare.Api.Query;
using Flare.Identity;
using Flare.Identity.Auth;
using Flare.Identity.Users;
using Flare.ServiceDefaults.ClickHouseMigrations;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Same connection name Flare.AppHost references onto Flare.Ingest - both projects read/
// write the same `clickhousedb.logs` table.
builder.AddClickHouseDataSource(connectionName: "clickhousedb");

// Redis: the live-tail endpoint's LogTailBroadcaster reads new entries off the same
// `flare:logs` stream Flare.Ingest's RedisStreamLogEventSink writes into. Same connection
// name Flare.AppHost references onto Flare.Ingest.
builder.AddRedisClient(connectionName: "redis");

// Auth + multi-user / roles (Planning.md's "Later" roadmap item). Full identity wiring
// (Users/Sessions/IngestApiKeys stores) - see Flare.Identity's remarks for why this is a
// separate shared project rather than living directly in this one.
builder.AddFlareIdentity();

var authenticationBuilder = builder.Services
    .AddAuthentication(SessionAuthenticationDefaults.SchemeName)
    .AddScheme<SessionAuthenticationSchemeOptions, SessionAuthenticationHandler>(SessionAuthenticationDefaults.SchemeName, _ => { });

// Microsoft Entra ID (SSO) - only registered when configured (Auth:Entra:Enabled),
// same opt-in-by-config precedent Auth:IngestKeyRequired already uses, so an upgraded
// deployment with no Entra config isn't suddenly asked for a TenantId/ClientId/
// ClientSecret it doesn't have. Read directly off configuration here (not via the
// IOptions<EntraOptions> registered by AddFlareIdentity above) because scheme
// registration happens at builder time, before the DI container exists to resolve
// IOptions from - see docs/auth.md's "Microsoft Entra ID (SSO)" section for the full
// design, including why SignInScheme is a short-lived paired cookie rather than trying
// to do Flare's own session-minting inside an OnTicketReceived event.
var entraOptions = builder.Configuration.GetSection(EntraOptions.SectionName).Get<EntraOptions>() ?? new EntraOptions();
if (entraOptions.Enabled)
{
    authenticationBuilder
        .AddCookie(EntraAuthenticationDefaults.ExternalCookieScheme, o =>
        {
            o.Cookie.Name = "flare_entra_external";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Lax;
            o.ExpireTimeSpan = TimeSpan.FromMinutes(10);
        })
        .AddOpenIdConnect(EntraAuthenticationDefaults.SchemeName, o =>
        {
            o.SignInScheme = EntraAuthenticationDefaults.ExternalCookieScheme;
            o.Authority = $"https://login.microsoftonline.com/{entraOptions.TenantId}/v2.0";
            o.ClientId = entraOptions.ClientId;
            o.ClientSecret = entraOptions.ClientSecret;
            o.ResponseType = OpenIdConnectResponseType.Code;
            o.UsePkce = true;
            // Flare never needs the id/access token again after this one exchange - the
            // ClaimsPrincipal is read once in EntraAuthEndpoints.HandleCompleteAsync and
            // discarded with the external cookie.
            o.SaveTokens = false;
            o.TokenValidationParameters.RoleClaimType = "roles";
            o.TokenValidationParameters.NameClaimType = "preferred_username";
        });
}

// Role set is intentionally small (Admin/Member/Viewer, see Flare.Identity.Users.UserRole) -
// the default policy (RequireAuthorization() with no policy name) already means "any
// authenticated user", i.e. Viewer-and-up, with no extra configuration needed here.
builder.Services.AddAuthorizationBuilder()
    .AddPolicy(AuthorizationPolicies.RequireMember, p => p.RequireRole(nameof(UserRole.Admin), nameof(UserRole.Member)))
    .AddPolicy(AuthorizationPolicies.RequireAdmin, p => p.RequireRole(nameof(UserRole.Admin)));

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ILogQueryService, LogQueryService>();
builder.Services.AddSingleton<ISpanQueryService, SpanQueryService>();
builder.Services.AddSingleton<IMetricQueryService, MetricQueryService>();
builder.Services.AddSingleton<IAlertQueryService, AlertQueryService>();
builder.Services.AddSingleton<ISavedViewQueryService, SavedViewQueryService>();
builder.Services.AddSingleton<IIngestionStatsQueryService, IngestionStatsQueryService>();
builder.Services.AddSingleton<IIndexingQueryService, IndexingQueryService>();
builder.Services.AddSingleton<IPipelineQueryService, PipelineQueryService>();
builder.Services.Configure<LiveTailOptions>(builder.Configuration.GetSection(LiveTailOptions.SectionName));
builder.Services.AddSingleton<LogTailBroadcaster>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LogTailBroadcaster>());

builder.Services.Configure<AlertingOptions>(builder.Configuration.GetSection(AlertingOptions.SectionName));
builder.Services.Configure<EmailOptions>(builder.Configuration.GetSection(EmailOptions.SectionName));
// Named/typed HttpClients so the webhook/Slack and Telegram senders inherit
// AddServiceDefaults()'s ConfigureHttpClientDefaults (resilience handler + service
// discovery) for free. Registered as their own concrete types, not IAlertNotifier -
// CompositeAlertNotifier (the one actually registered as IAlertNotifier below) holds all
// three and picks per-rule which one to delegate to. EmailAlertNotifier gets no typed
// HttpClient - MailKit's SmtpClient is its own socket-based client, not HTTP.
builder.Services.AddHttpClient<WebhookAlertNotifier>("alert-webhook");
builder.Services.AddHttpClient<TelegramAlertNotifier>("alert-telegram");
builder.Services.AddSingleton<EmailAlertNotifier>();
builder.Services.AddSingleton<IAlertNotifier, CompositeAlertNotifier>();
builder.Services.AddHostedService<AlertEvaluationWorker>();

builder.Services.AddOpenApi();

// Auth landed - AllowAnyOrigin() is no longer safe now that requests carry a session
// cookie (AllowCredentials() and AllowAnyOrigin() are mutually exclusive by the CORS
// spec/ASP.NET Core's own enforcement anyway, so this couldn't stay permissive even
// unintentionally). Cors:AllowedOrigins must list the dashboard's actual origin(s) -
// e.g. http://localhost:5173 in dev, http://localhost:3000 in docker-compose (dashboard
// and API are different origins even there, so this can't default to same-origin-only).
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy => policy
        .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
        .AllowAnyMethod()
        .AllowAnyHeader()
        .AllowCredentials());
});

var app = builder.Build();

// Apply any pending db/clickhouse/*.sql migrations before mapping any endpoints below
// (they assume the schema already exists) - see ClickHouseMigrationRunner's remarks for
// why docker-entrypoint-initdb.d alone isn't enough once a deployment has real data on
// disk. Safe to run unconditionally on every startup: every migration is idempotent,
// and safe to run from both Flare.Api and Flare.Ingest independently (no ordering
// requirement between them).
await ClickHouseMigrationRunner.ApplyAsync(
    app.Services.GetRequiredService<IClickHouseClient>(),
    app.Logger,
    CancellationToken.None);

await IdentityMigrationRunner.ApplyAsync(
    app.Services.GetRequiredService<IdentityDbConnectionFactory>(),
    app.Logger,
    CancellationToken.None);

app.UseCors();
app.UseWebSockets();

// Must come after UseCors() (so a CORS preflight OPTIONS request isn't itself
// auth-challenged) and before every Map*Endpoints() call below.
app.UseAuthentication();
app.UseAuthorization();

app.MapDefaultEndpoints();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Unauthenticated by design - a client can't have a session yet when calling these.
app.MapAuthEndpoints();
app.MapEntraAuthEndpoints();

// MapGroup("") is a routing no-op (empty prefix) used purely to get a convention
// builder to hang RequireAuthorization() off of - every Map*Endpoints() call below is
// itself just `this IEndpointRouteBuilder endpoints => ... return endpoints;`, so
// mapping them onto this group (rather than directly onto `app`) makes their routes
// inherit the group's authorization requirement with no changes needed in any of those
// endpoint files.
var authenticatedRoutes = app.MapGroup("").RequireAuthorization();
authenticatedRoutes.MapLogsEndpoints();
authenticatedRoutes.MapLogTailEndpoints();
authenticatedRoutes.MapSpanEndpoints();
authenticatedRoutes.MapMetricsEndpoints();
authenticatedRoutes.MapSavedViewEndpoints();
authenticatedRoutes.MapIngestionEndpoints();
authenticatedRoutes.MapPipelineEndpoints();
authenticatedRoutes.MapIndexingEndpoints();

// Alert rule CRUD/test-fire is mutating and can page people - Member/Admin only, unlike
// every other (read-only) endpoint group above which just needs any authenticated user.
var memberRoutes = app.MapGroup("").RequireAuthorization(AuthorizationPolicies.RequireMember);
memberRoutes.MapAlertEndpoints();

// Ingest API key issuance/revocation is Admin-only - a leaked key lets any caller ingest
// telemetry as this Flare instance, so this isn't something a Member should be able to
// self-service.
var adminRoutes = app.MapGroup("").RequireAuthorization(AuthorizationPolicies.RequireAdmin);
adminRoutes.MapIngestApiKeyEndpoints();
adminRoutes.MapUserEndpoints();

app.Run();
