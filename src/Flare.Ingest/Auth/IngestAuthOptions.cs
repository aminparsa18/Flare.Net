namespace Flare.Ingest.Auth;

/// <summary>Bound from the <c>Auth</c> configuration section - same top-level key
/// <c>Flare.Api</c> binds its own (differently-shaped) <see cref="Flare.Identity.Auth.AuthOptions"/>
/// from, since both processes are configuring "the auth story," just different halves of it.</summary>
public sealed class IngestAuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>False by default so existing anonymous-ingest deployments upgrade
    /// without breakage - flip once at least one key exists (see
    /// <c>POST /api/ingest-keys</c> on <c>Flare.Api</c>, or <see cref="StaticIngestApiKey"/>
    /// below) and every OTLP exporter pointed at this instance has been updated to send one.</summary>
    public bool IngestKeyRequired { get; set; }

    /// <summary>
    /// An optional fixed key, valid in addition to (not instead of) any SQLite-backed
    /// keys created via <c>POST /api/ingest-keys</c>. This is the mechanism Aspire
    /// orchestration pins a key through - <c>Flare.AppHost</c> for local dev,
    /// <c>Aspire.Hosting.Flare</c>'s <c>AddFlare(...).WithApiKey(...)</c> for consumers of
    /// the published integration package - since "create a key via the dashboard" is a
    /// manual, human-driven flow that doesn't fit an automated resource-graph-wiring use
    /// case. Set via configuration (a secret Aspire parameter, an env var, etc.), never
    /// created/revoked through the API the way SQLite-backed keys are.
    /// </summary>
    public string? StaticIngestApiKey { get; set; }
}
