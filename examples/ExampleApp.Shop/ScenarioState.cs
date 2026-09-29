namespace ExampleApp.Shop;

/// <summary>
/// The shop's failure-mode switches. Each process keeps its own copy and only acts on the
/// flags that concern it; <c>POST /scenario</c> on storefront fans a change out to every
/// other shop service (see <see cref="ScenarioEndpoints"/>), so one call flips the whole shop.
/// Initial values come from the <c>Shop:Scenario</c> config section, e.g.
/// <c>Shop__Scenario__PartnerOutage=true</c>.
/// </summary>
/// <remarks>
/// What each one does, and which Flare feature it's there to exercise:
/// <list type="bullet">
/// <item><see cref="ScenarioFlags.LatencySpike"/> - payment-service stalls before charging and
/// api.stripe.com answers 4x slower: p95 alerts, anomaly detection, the Services latency chart.</item>
/// <item><see cref="ScenarioFlags.PartnerOutage"/> - inventory.partner-corp.com returns 503 for
/// most calls: the External APIs page's error rate, error-rate alerts.</item>
/// <item><see cref="ScenarioFlags.ConsumerSlowdown"/> - order-service's payment-reconciler and
/// notification-service's order-notifier Kafka groups slow to a crawl: growing consumer lag on
/// the Message queues page (via the otelcol-contrib kafkametrics receiver).</item>
/// <item><see cref="ScenarioFlags.CrashLoop"/> - inventory-service's stock-sync worker keeps
/// crashing on startup and the service answers 503 while it "restarts": a Critical log burst,
/// the Errors page, absent-data and error-count alerts. Simulated in-process, since Aspire
/// doesn't restart a crashed project - see <see cref="InventoryService"/>.</item>
/// </list>
/// </remarks>
public sealed class ScenarioState(IConfiguration configuration, ILogger<ScenarioState> logger)
{
    private readonly Lock _gate = new();
    private ScenarioFlags _flags = configuration.GetSection("Shop:Scenario").Get<ScenarioFlags>() ?? new ScenarioFlags();

    public ScenarioFlags Current
    {
        get
        {
            lock (_gate)
            {
                return _flags;
            }
        }
    }

    public ScenarioFlags Apply(ScenarioUpdate update)
    {
        lock (_gate)
        {
            var next = new ScenarioFlags
            {
                LatencySpike = update.LatencySpike ?? _flags.LatencySpike,
                PartnerOutage = update.PartnerOutage ?? _flags.PartnerOutage,
                ConsumerSlowdown = update.ConsumerSlowdown ?? _flags.ConsumerSlowdown,
                CrashLoop = update.CrashLoop ?? _flags.CrashLoop,
            };

            if (next != _flags)
            {
                logger.LogWarning(
                    "Scenario changed: latencySpike={LatencySpike} partnerOutage={PartnerOutage} consumerSlowdown={ConsumerSlowdown} crashLoop={CrashLoop}",
                    next.LatencySpike, next.PartnerOutage, next.ConsumerSlowdown, next.CrashLoop);
            }

            _flags = next;
            return next;
        }
    }
}

public sealed record ScenarioFlags
{
    public bool LatencySpike { get; init; }
    public bool PartnerOutage { get; init; }
    public bool ConsumerSlowdown { get; init; }
    public bool CrashLoop { get; init; }
}

/// <summary>A partial <see cref="ScenarioFlags"/> - <see langword="null"/> leaves that flag as it is.</summary>
public sealed record ScenarioUpdate
{
    public bool? LatencySpike { get; init; }
    public bool? PartnerOutage { get; init; }
    public bool? ConsumerSlowdown { get; init; }
    public bool? CrashLoop { get; init; }

    public static ScenarioUpdate Reset { get; } = new() { LatencySpike = false, PartnerOutage = false, ConsumerSlowdown = false, CrashLoop = false };

    public static ScenarioUpdate? For(string name, bool on) => name.ToLowerInvariant() switch
    {
        "latency-spike" => new() { LatencySpike = on },
        "partner-outage" => new() { PartnerOutage = on },
        "consumer-slowdown" => new() { ConsumerSlowdown = on },
        "crash-loop" => new() { CrashLoop = on },
        _ => null,
    };
}

public static class ScenarioEndpoints
{
    /// <summary>Marks a request storefront forwarded, so the receiving service applies it without fanning it out again.</summary>
    private const string ForwardedHeader = "X-Shop-Scenario-Forwarded";

    public static void MapScenarioEndpoints(this WebApplication app, ShopRole role)
    {
        app.MapGet("/scenario", (ScenarioState state) => state.Current);

        // Partial JSON body, e.g. {"partnerOutage": true}.
        app.MapPost("/scenario", (ScenarioUpdate update, HttpContext context, ScenarioState state, IHttpClientFactory clients, IConfiguration configuration) =>
            ApplyAsync(update, role, context, state, clients, configuration));

        // Same thing as a path, for curl and the Aspire dashboard's resource commands:
        // POST /scenario/partner-outage/on, POST /scenario/reset.
        app.MapPost("/scenario/{name}/{state:regex(^(on|off)$)}", (string name, string state, HttpContext context, ScenarioState scenario, IHttpClientFactory clients, IConfiguration configuration) =>
            ScenarioUpdate.For(name, state == "on") is { } update
                ? ApplyAsync(update, role, context, scenario, clients, configuration)
                : Task.FromResult(Results.NotFound(new { error = $"Unknown scenario '{name}'. Use latency-spike, partner-outage, consumer-slowdown or crash-loop." })));

        app.MapPost("/scenario/reset", (HttpContext context, ScenarioState state, IHttpClientFactory clients, IConfiguration configuration) =>
            ApplyAsync(ScenarioUpdate.Reset, role, context, state, clients, configuration));
    }

    private static async Task<IResult> ApplyAsync(
        ScenarioUpdate update, ShopRole role, HttpContext context, ScenarioState state, IHttpClientFactory clients, IConfiguration configuration)
    {
        var flags = state.Apply(update);
        if (role != ShopRole.Storefront || context.Request.Headers.ContainsKey(ForwardedHeader))
        {
            return Results.Ok(flags);
        }

        // Every shop service storefront holds a reference to (services__<name>__http__0,
        // injected by the AppHost's WithReference) gets the same update.
        var client = clients.CreateClient();
        var forwarded = new Dictionary<string, string>();
        foreach (var peer in configuration.GetSection("services").GetChildren().Select(s => s.Key))
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, $"http://{peer}/scenario") { Content = JsonContent.Create(update) };
                request.Headers.Add(ForwardedHeader, "1");
                using var response = await client.SendAsync(request, context.RequestAborted);
                forwarded[peer] = ((int)response.StatusCode).ToString();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                forwarded[peer] = ex.GetType().Name;
            }
        }

        return Results.Ok(new { scenario = flags, forwarded });
    }
}
