using ExampleApp.Seeder;
using ExampleApp.Seeder.Scenarios;

// Backfills an hour (by default) of realistic, backdated telemetry into a running Flare, one
// scenario per docs page - see examples/README.md. Everything goes in through the public OTLP/
// HTTP endpoint (:4318) and Flare.Api, like any other client; only clearing talks to
// ClickHouse directly, since Flare has no API for deleting telemetry.

Scenario[] scenarios =
[
    new OverviewScenario(),
    new FunnelScenario(),
    new StructureScenario(),
    new ExternalScenario(),
    new CardinalityScenario(),
    new MessagingScenario(),
    new HostsScenario(),
    new KubernetesScenario(),
    new PipelineScenario(),
];

SeederOptions options;
try
{
    options = SeederOptions.Parse(args, scenarios.Select(s => s.Name).ToHashSet());
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine();
    SeederOptions.PrintUsage(scenarios);
    return 2;
}

if (options.ShowHelp || options.Scenarios.Count == 0)
{
    SeederOptions.PrintUsage(scenarios);
    return options.ShowHelp ? 0 : 2;
}

var selected = options.Scenarios.Contains("all") ? scenarios : scenarios.Where(s => options.Scenarios.Contains(s.Name)).ToArray();
var names = selected.Select(s => s.Name).ToArray();

using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};
var ct = cts.Token;

var api = new FlareApiClient(http, options.Api, options.Token);
var sender = new OtlpSender(http, options.Otlp, options.IngestKey);
var cleaner = new ClickHouseCleaner(http, options.ClickHouse, options.ClickHouseUser, options.ClickHousePassword, options.ClickHouseDatabase);

if (options.DryRun)
{
    // Objects are built and "created" against a stub that just prints each request.
    using var stubHttp = new HttpClient(new DryRunHandler());
    var stubApi = new FlareApiClient(stubHttp, options.Api, null);
    foreach (var scenario in selected)
    {
        var context = new SeedContext(scenario.Name, options.Minutes, options.Locale, options.Seed ?? StableSeed(scenario.Name), stubApi);
        scenario.Generate(context);
        Console.WriteLine($"{scenario.Name,-12} {context.Batch.Summary()}");
        await scenario.CreateObjectsAsync(context, CancellationToken.None);
    }

    return 0;
}

try
{
    if (options.Clear || !options.Append)
    {
        Console.WriteLine($"Clearing previously seeded {string.Join(", ", names)} data...");
        await cleaner.ClearAsync(names, ct);
        foreach (var scenario in selected)
        {
            foreach (var group in scenario.Objects.GroupBy(o => o.Kind))
            {
                var deleted = await api.DeleteByNameAsync(group.Key, group.SelectMany(o => o.Name.All), ct);
                if (deleted > 0)
                {
                    Console.WriteLine($"  deleted {deleted} {group.Key} ({scenario.Name})");
                }
            }
        }

        if (options.Clear)
        {
            Console.WriteLine("Done.");
            return 0;
        }
    }

    // Objects first for every scenario, then one wait for the slowest to settle, then all the
    // telemetry - so a pipeline rule is live before the logs it's meant to rewrite arrive.
    var contexts = new List<(Scenario Scenario, SeedContext Context)>();
    foreach (var scenario in selected)
    {
        var context = new SeedContext(scenario.Name, options.Minutes, options.Locale, options.Seed ?? StableSeed(scenario.Name), api);
        scenario.Generate(context);
        Console.WriteLine($"{scenario.Name}: {scenario.Description}");
        await scenario.CreateObjectsAsync(context, ct);
        contexts.Add((scenario, context));
    }

    var settle = selected.Max(s => s.IngestSettleTime);
    if (settle > TimeSpan.Zero)
    {
        Console.WriteLine($"Waiting {settle.TotalSeconds:F0} s for Flare.Ingest to load the new pipeline rules...");
        await Task.Delay(settle, ct);
    }

    foreach (var (scenario, context) in contexts)
    {
        await context.Batch.SendAsync(sender, ct);
        Console.WriteLine($"  sent {scenario.Name} ({context.Batch.SpanCount} spans)");
    }

    Console.WriteLine(
        $"Done - {options.Minutes} min of data ending now. Flare.Ingest flushes to ClickHouse in batches, so allow a few seconds before it all shows up.");
    return 0;
}
catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    if (ex is HttpRequestException)
    {
        Console.Error.WriteLine(
            "Is Flare running? Defaults match `docker compose up` (OTLP :4318, API :8080, ClickHouse :8123). " +
            "Under Aspire, pass the ports `aspire describe` shows; without ClickHouse access, use --append.");
    }

    return 1;
}
catch (OperationCanceledException)
{
    return 130;
}

static int StableSeed(string name) => name.Aggregate(17, (hash, ch) => hash * 31 + ch);

/// <summary>--dry-run's stand-in for Flare.Api: prints the request, answers as if it was created.</summary>
internal sealed class DryRunHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Console.WriteLine($"    would {request.Method} {request.RequestUri!.AbsolutePath} ({body.Length} bytes)");
        return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("""{"id":"dry-run"}""") };
    }
}
