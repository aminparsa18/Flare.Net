namespace Flare.Identity;

/// <summary>
/// Where the embedded identity SQLite database file lives. Bound from the
/// <c>Identity</c> configuration section (<c>Identity:DbPath</c> /
/// <c>Identity__DbPath</c>) - both <c>Flare.Api</c> and <c>Flare.Ingest</c> must resolve
/// this to the identical absolute path, since they open the same file (see
/// docker-compose.yml's shared <c>identity-data</c> volume and
/// <c>Flare.AppHost/AppHost.cs</c>'s equivalent local-dev wiring).
/// </summary>
public sealed class IdentityOptions
{
    public const string SectionName = "Identity";

    /// <summary>
    /// Filesystem path to the SQLite database file. Defaults to a path relative to the
    /// process's working directory for local `dotnet run` - every real deployment
    /// (docker-compose, Aspire) overrides this to a volume-backed absolute path.
    /// </summary>
    public string DbPath { get; set; } = "flare-identity.db";
}
