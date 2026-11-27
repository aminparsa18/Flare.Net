using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Flare.ServiceDefaults.ClickHouseClients;

/// <summary>
/// The ClickHouse connection names Flare's services use, one per ClickHouse user (ADR-0282), and the
/// registrations that read them.
/// </summary>
/// <remarks>
/// <list type="table">
/// <item><term><c>clickhousedb</c></term><description>The DDL credential (<c>default</c>). Used only by
/// <c>ClickHouseMigrationRunner</c> at startup, through the keyed client <see cref="DdlConnectionName"/>.</description></item>
/// <item><term><c>clickhousedb-ingest</c></term><description><c>flare_ingest</c>: Flare.Ingest's plain <c>IClickHouseClient</c>.</description></item>
/// <item><term><c>clickhousedb-query</c></term><description><c>flare_query</c>: the keyed client <see cref="QueryConnectionName"/>
/// that Flare.Api's read-only query services take.</description></item>
/// <item><term><c>clickhousedb-app</c></term><description><c>flare_app</c>: the plain <c>IClickHouseClient</c> in Flare.Api
/// (config writes, retention, promoted columns, erasure) and Flare.AlertWorker.</description></item>
/// </list>
/// Each of the three service connection strings falls back to <c>clickhousedb</c> when it isn't set
/// (or is empty), so an install without the Flare users keeps running everything as <c>default</c>.
/// </remarks>
public static class FlareClickHouse
{
    public const string DdlConnectionName = "clickhousedb";
    public const string IngestConnectionName = "clickhousedb-ingest";
    public const string QueryConnectionName = "clickhousedb-query";
    public const string AppConnectionName = "clickhousedb-app";

    /// <summary>
    /// Registers the plain <c>IClickHouseClient</c> (and <c>ClickHouseDataSource</c>) on
    /// <paramref name="connectionName"/>, falling back to <see cref="DdlConnectionName"/>.
    /// </summary>
    public static void AddFlareClickHouseClient(this IHostApplicationBuilder builder, string connectionName) =>
        builder.AddClickHouseDataSource(
            connectionName,
            settings => settings.ConnectionString = ResolveConnectionString(builder.Configuration, connectionName) ?? settings.ConnectionString);

    /// <summary>
    /// Registers an <c>IClickHouseClient</c> keyed by <paramref name="connectionName"/>, falling back to
    /// <see cref="DdlConnectionName"/>. <paramref name="healthCheck"/> is off for the migration client,
    /// which is idle once startup is done.
    /// </summary>
    public static void AddKeyedFlareClickHouseClient(this IHostApplicationBuilder builder, string connectionName, bool healthCheck = true) =>
        builder.AddKeyedClickHouseDataSource(
            connectionName,
            settings =>
            {
                settings.ConnectionString = ResolveConnectionString(builder.Configuration, connectionName) ?? settings.ConnectionString;
                settings.DisableHealthChecks |= !healthCheck;
            });

    /// <summary>
    /// Makes the keyed <see cref="QueryConnectionName"/> client resolve to the plain one, for a process
    /// that shares Flare.Api's query services but runs them all as one user (Flare.AlertWorker).
    /// </summary>
    public static void AddQueryClientAlias(this IServiceCollection services) =>
        services.AddKeyedSingleton(
            QueryConnectionName,
            static (sp, _) => sp.GetRequiredService<global::ClickHouse.Driver.IClickHouseClient>());

    /// <summary>
    /// <c>ConnectionStrings:{connectionName}</c> when it's set and not empty, else
    /// <c>ConnectionStrings:clickhousedb</c>; null when neither is.
    /// </summary>
    public static string? ResolveConnectionString(IConfiguration configuration, string connectionName)
    {
        var own = configuration.GetConnectionString(connectionName);
        if (!string.IsNullOrWhiteSpace(own))
        {
            return own;
        }

        var fallback = configuration.GetConnectionString(DdlConnectionName);
        return string.IsNullOrWhiteSpace(fallback) ? null : fallback;
    }
}
