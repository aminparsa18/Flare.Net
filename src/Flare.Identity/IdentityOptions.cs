namespace Flare.Identity;

/// <summary>Which database engine backs the identity/auth tables (ADR-0109, which
/// supersedes ADR-0004's SQLite-only decision).</summary>
public enum IdentityProvider
{
    /// <summary>Embedded SQLite file (<see cref="IdentityOptions.DbPath"/>). The default:
    /// no extra container, but a single <c>Flare.Api</c> replica.</summary>
    Sqlite,

    /// <summary>An external PostgreSQL server (<see cref="IdentityOptions.ConnectionString"/>).
    /// Lets several <c>Flare.Api</c> replicas share one identity store.</summary>
    Postgres,
}

/// <summary>
/// Where the identity database lives. Bound from the <c>Identity</c> configuration
/// section (<c>Identity:Provider</c> / <c>Identity__Provider</c>, etc.) - both
/// <c>Flare.Api</c> and <c>Flare.Ingest</c> must resolve this to the same database: the
/// identical absolute <see cref="DbPath"/> for SQLite (see docker-compose.yml's shared
/// <c>identity-data</c> volume and <c>Flare.AppHost/AppHost.cs</c>'s equivalent local-dev
/// wiring), or the same <see cref="ConnectionString"/> for Postgres.
/// </summary>
public sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    /// <summary>Database engine. Defaults to <see cref="IdentityProvider.Sqlite"/>.</summary>
    public IdentityProvider Provider { get; set; } = IdentityProvider.Sqlite;

    /// <summary>
    /// Filesystem path to the SQLite database file (<see cref="IdentityProvider.Sqlite"/>
    /// only). Defaults to a path relative to the process's working directory for local
    /// `dotnet run` - every real deployment (docker-compose, Aspire) overrides this to a
    /// volume-backed absolute path.
    /// </summary>
    public string DbPath { get; set; } = "flare-identity.db";

    /// <summary>Npgsql connection string (<see cref="IdentityProvider.Postgres"/> only),
    /// e.g. <c>Host=postgres;Database=flare_identity;Username=flare;Password=...</c>.</summary>
    public string? ConnectionString { get; set; }
}
