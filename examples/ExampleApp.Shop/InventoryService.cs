using Confluent.Kafka;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace ExampleApp.Shop;

/// <summary>
/// Stock levels over the <c>products</c> table: lookups, search, and reservations (announced on
/// Kafka). Also home to the crash-loop scenario - see <see cref="StockSyncWorker"/>.
/// </summary>
public static class InventoryService
{
    public static void AddServices(WebApplicationBuilder builder)
    {
        builder.AddShopDatabase();
        builder.AddKafkaProducer();
        builder.Services.AddSingleton<StockLedger>();
        builder.Services.AddHostedService<StockSyncWorker>();
        builder.Services.AddHealthChecks().AddCheck<StockLedger>("stock-ledger");
    }

    public static void Map(WebApplication app)
    {
        app.MapGet("/inventory/{sku:int}", async (int sku, NpgsqlDataSource db, StockLedger ledger, CancellationToken ct) =>
        {
            ledger.EnsureLoaded();
            await using var select = db.CreateCommand("SELECT name, price, stock FROM products WHERE sku = $1");
            select.Parameters.Add(new() { Value = sku });
            await using var reader = await select.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct)
                ? Results.Ok(new { sku, name = reader.GetString(0), price = reader.GetDecimal(1), stock = reader.GetInt32(2) })
                : Results.NotFound();
        });

        app.MapGet("/inventory/search", async (string q, NpgsqlDataSource db, StockLedger ledger, CancellationToken ct) =>
        {
            ledger.EnsureLoaded();
            await using var select = db.CreateCommand("SELECT sku, name FROM products WHERE name ILIKE $1 ORDER BY sku LIMIT 20");
            select.Parameters.Add(new() { Value = $"%{q}%" });
            await using var reader = await select.ExecuteReaderAsync(ct);
            var hits = new List<object>();
            while (await reader.ReadAsync(ct))
            {
                hits.Add(new { sku = reader.GetInt32(0), name = reader.GetString(1) });
            }

            return Results.Ok(hits);
        });

        app.MapPost("/inventory/reserve", async (ReserveRequest request, NpgsqlDataSource db, StockLedger ledger, IProducer<string, string> kafka, CancellationToken ct) =>
        {
            ledger.EnsureLoaded();
            decimal total = 0;
            var partnerSkus = new List<int>();
            foreach (var line in request.Items)
            {
                await using var update = db.CreateCommand(
                    "UPDATE products SET stock = GREATEST(stock - $2, 0) WHERE sku = $1 RETURNING price, partner_sku");
                update.Parameters.Add(new() { Value = line.Sku });
                update.Parameters.Add(new() { Value = line.Quantity });
                await using var reader = await update.ExecuteReaderAsync(ct);
                if (await reader.ReadAsync(ct))
                {
                    total += reader.GetDecimal(0) * line.Quantity;
                    if (!reader.IsDBNull(1))
                    {
                        partnerSkus.Add(reader.GetInt32(1));
                    }
                }
            }

            await kafka.ProduceAsync(KafkaClients.InventoryReserved, new Message<string, string>
            {
                Key = request.OrderId.ToString(),
                Value = new OrderEvent(request.OrderId, 0, total, "USD").ToJson(),
            }, ct);

            return Results.Ok(new ReserveResult(total, partnerSkus));
        });
    }
}

/// <summary>
/// The in-memory stock ledger stock-sync keeps loaded. While it's down, every inventory
/// request throws - an unhandled exception per request (so the Errors page and the error rate
/// light up) - and the health check reports Unhealthy.
/// </summary>
public sealed class StockLedger : IHealthCheck
{
    private volatile bool _loaded = true;

    public void MarkLoaded() => _loaded = true;

    public void MarkUnloaded() => _loaded = false;

    public void EnsureLoaded()
    {
        if (!_loaded)
        {
            throw new InvalidOperationException("Stock ledger is not loaded: stock-sync is restarting.");
        }
    }

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(_loaded ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("stock-sync is crash-looping"));
}

/// <summary>
/// Syncs stock from the warehouse ledger. Under <see cref="ScenarioFlags.CrashLoop"/> it plays
/// out a crash loop: start, fail a few seconds in with an unhandled exception, back off
/// (10 s, 20 s, 40 s, capped at 60 s), start again.
/// </summary>
/// <remarks>
/// Simulated in-process rather than by actually exiting: Aspire doesn't restart a project
/// resource that exits, so a real crash would just stop the demo. The logs are the ones a
/// real crash loop produces - a Critical "Application is terminating" with the stack trace,
/// then the orchestrator's back-off message.
/// </remarks>
public sealed class StockSyncWorker(ScenarioState scenario, StockLedger ledger, ILogger<StockSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var restarts = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!scenario.Current.CrashLoop)
            {
                if (restarts > 0)
                {
                    logger.LogInformation("stock-sync recovered after {Restarts} restarts", restarts);
                    restarts = 0;
                }

                ledger.MarkLoaded();
                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                continue;
            }

            logger.LogInformation("Starting stock-sync (restart {Restarts})", restarts);
            ledger.MarkLoaded();
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            try
            {
                ReplayLedger("wh-3");
            }
            catch (Exception ex)
            {
                ledger.MarkUnloaded();
                logger.LogCritical(ex, "Unhandled exception. Application is terminating.");
                restarts++;
                var backOff = TimeSpan.FromSeconds(Math.Min(60, 10 * Math.Pow(2, Math.Min(restarts - 1, 3))));
                logger.LogWarning("Back-off restarting failed stock-sync, restart {Restarts} in {BackOffSeconds}s", restarts, backOff.TotalSeconds);
                await Task.Delay(backOff, stoppingToken);
            }
        }
    }

    // A few real frames deep, so the stack trace reads like one.
    private static void ReplayLedger(string warehouse) => VerifySegment(warehouse, segment: 7);

    private static void VerifySegment(string warehouse, int segment) =>
        throw new InvalidDataException($"Stock ledger checksum mismatch in segment {segment} for warehouse {warehouse}: expected 0x5F3A91C2, found 0x00000000");
}
