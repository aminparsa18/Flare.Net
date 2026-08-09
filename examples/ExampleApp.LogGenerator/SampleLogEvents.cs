namespace ExampleApp.LogGenerator;

/// <summary>
/// A small hand-authored pool of realistic-looking log events, picked at random by
/// <see cref="RandomLogGeneratorWorker"/> - stands in for "your actual app's logging
/// calls" so the Flare dashboard has varied, structured, occasionally-exceptional data
/// to show off filtering, live tail, and the event-detail panel against. No fake-data
/// library (e.g. Bogus) - this pool is small enough to hand-write and keeps the example
/// dependency-free.
/// </summary>
internal static class SampleLogEvents
{
    private static readonly string[] Users = ["alice", "bob", "carol", "dave", "erin"];
    private static readonly string[] Products = ["widget", "gadget", "gizmo", "doohickey"];
    private static readonly string[] Tables = ["orders", "users", "products", "inventory"];
    private static readonly string[] Paths = ["/api/products", "/api/orders", "/api/users", "/api/search"];
    private static readonly string[] JobNames = ["SendWelcomeEmail", "SyncInventory", "GenerateInvoice", "RefreshCache"];

    /// <summary>Logs one randomly-selected event: ~70% Information, ~20% Warning, ~10% Error/Critical.</summary>
    public static void EmitOne(ILogger logger)
    {
        switch (Random.Shared.Next(100))
        {
            case < 30:
                EmitOrderProcessed(logger);
                break;
            case < 50:
                EmitCacheEvent(logger);
                break;
            case < 70:
                EmitRequestCompleted(logger);
                break;
            case < 90:
                EmitSlowQuery(logger);
                break;
            case < 97:
                EmitBackgroundJobFailed(logger);
                break;
            default:
                EmitCriticalFailure(logger);
                break;
        }
    }

    private static void EmitOrderProcessed(ILogger logger)
    {
        var orderId = Random.Shared.Next(10000, 99999);
        var durationMs = Random.Shared.Next(20, 400);
        logger.LogInformation(
            "Processed order {OrderId} for {User} ({Product}) in {DurationMs}ms",
            orderId, Pick(Users), Pick(Products), durationMs);
    }

    private static void EmitCacheEvent(ILogger logger)
    {
        var key = $"{Pick(Products)}:{Random.Shared.Next(1, 500)}";
        if (Random.Shared.Next(2) == 0)
        {
            logger.LogInformation("Cache hit for {CacheKey}", key);
        }
        else
        {
            logger.LogInformation("Cache miss for {CacheKey}, fetching from origin", key);
        }
    }

    private static void EmitRequestCompleted(ILogger logger)
    {
        var statusCode = Random.Shared.Next(10) == 0 ? 404 : 200;
        var durationMs = Random.Shared.Next(5, 250);
        logger.LogInformation(
            "{Method} {Path} responded {StatusCode} in {DurationMs}ms",
            "GET", Pick(Paths), statusCode, durationMs);
    }

    private static void EmitSlowQuery(ILogger logger)
    {
        var durationMs = Random.Shared.Next(800, 3000);
        logger.LogWarning(
            "Query against {Table} took {DurationMs}ms, exceeding the 500ms threshold",
            Pick(Tables), durationMs);
    }

    private static void EmitBackgroundJobFailed(ILogger logger)
    {
        var jobName = Pick(JobNames);
        try
        {
            // Actually thrown (not just `new`'d) so .StackTrace is populated - the CLR only
            // fills that in during unwind. A merely-constructed exception has a null
            // StackTrace, so exception.stacktrace never reaches the OTel log record and the
            // dashboard's event-detail panel has nothing to show.
            throw new InvalidOperationException($"Job '{jobName}' could not acquire the distributed lock in time.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Background job {JobName} failed", jobName);
        }
    }

    private static void EmitCriticalFailure(ILogger logger)
    {
        try
        {
            throw new TimeoutException("Downstream payment provider did not respond within 5000ms.");
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Payment provider call timed out - failing over to secondary");
        }
    }

    private static string Pick(IReadOnlyList<string> options) => options[Random.Shared.Next(options.Count)];
}
