using ClickHouse.Driver;
using Flare.ServiceDefaults.ClickHouseClients;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Flare.ServiceDefaults.ClickHouseMigrations;

/// <summary>
/// Who applies the ClickHouse migrations (ADR-0284): a process that holds the DDL credential
/// (<c>ConnectionStrings:clickhousedb</c>) applies them at startup, as before; one that doesn't waits for the
/// one-shot migration job (<see cref="RunMigrateCommandAsync"/>) to have applied them.
/// </summary>
public static class ClickHouseSchemaStartup
{
    /// <summary>The first argument that runs Flare.Api as the migration job instead of the API.</summary>
    public const string MigrateCommand = "migrate";

    /// <summary>How long a process without the DDL credential waits for the job. Default 300.</summary>
    public const string WaitTimeoutSecondsKey = "ClickHouse:SchemaWaitTimeoutSeconds";

    /// <summary>
    /// Applies the migrations through the keyed DDL client when <c>ConnectionStrings:clickhousedb</c> is set,
    /// else waits for them through <paramref name="runtimeClient"/>, which must be able to read
    /// <c>clickhousedb.schema_migrations</c>.
    /// </summary>
    public static async Task ApplyOrWaitAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IClickHouseClient runtimeClient,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var clusterMode = configuration.GetValue<bool>("ClickHouse:ClusterMode");
        if (FlareClickHouse.HasDdlConnection(configuration))
        {
            await ClickHouseMigrationRunner.ApplyAsync(
                services.GetRequiredKeyedService<IClickHouseClient>(FlareClickHouse.DdlConnectionName),
                logger,
                cancellationToken,
                clusterMode);
            return;
        }

        var timeout = TimeSpan.FromSeconds(configuration.GetValue(WaitTimeoutSecondsKey, 300));
        await ClickHouseMigrationRunner.WaitForSchemaAsync(runtimeClient, logger, timeout, cancellationToken, clusterMode);
    }

    /// <summary>
    /// The one-shot migration job: applies every pending migration as <c>ConnectionStrings:clickhousedb</c> and
    /// returns the process exit code. <paramref name="args"/> are the arguments after <c>migrate</c>, read as
    /// configuration like any other host's.
    /// </summary>
    public static async Task<int> RunMigrateCommandAsync(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.AddServiceDefaults();
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddSimpleConsole(o => o.SingleLine = true));
        var logger = loggerFactory.CreateLogger("Flare.ClickHouseMigrations");

        if (!FlareClickHouse.HasDdlConnection(builder.Configuration))
        {
            logger.LogError("The migrate command needs ConnectionStrings:clickhousedb, the DDL credential (the `default` user).");
            return 2;
        }

        builder.AddFlareClickHouseClient(FlareClickHouse.DdlConnectionName);
        using var host = builder.Build();
        var clusterMode = builder.Configuration.GetValue<bool>("ClickHouse:ClusterMode");
        try
        {
            await ClickHouseMigrationRunner.ApplyAsync(
                host.Services.GetRequiredService<IClickHouseClient>(), logger, CancellationToken.None, clusterMode);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ClickHouse migrations failed.");
            return 1;
        }

        logger.LogInformation(
            "ClickHouse schema is current ({Count} migrations).", ClickHouseMigrationRunner.EmbeddedMigrationNames(clusterMode).Count);
        return 0;
    }
}
