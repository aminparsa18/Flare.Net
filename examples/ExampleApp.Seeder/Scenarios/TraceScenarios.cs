using System.Text.Json.Nodes;
using static ExampleApp.Seeder.SeedContext;

namespace ExampleApp.Seeder.Scenarios;

/// <summary>
/// storefront → checkout-api → payment-service → order-service chains that drop off at each step
/// and fail most at payments, plus a saved funnel over them. For the Trace funnels docs.
/// </summary>
public sealed class FunnelScenario : Scenario
{
    public override string Name => "funnel";

    public override string Description => "Checkout chains with drop-off and payment failures, plus a saved funnel";

    private static readonly Localized ViewName = new("Checkout funnel", "Воронка оформления заказа", "结账漏斗");

    public override IEnumerable<(FlareApiClient.Kind, Localized)> Objects => [(FlareApiClient.Kind.Views, ViewName)];

    // service, span name, chance of reaching this step from the previous one, error chance, mean gap (ms)
    private static readonly (string Service, string Span, double Reach, double Error, double GapMs)[] Steps =
    [
        ("storefront", "GET /cart", 1.0, 0.0, 0),
        ("checkout-api", "POST /checkout", 0.72, 0.02, 180),
        ("payment-service", "POST /payments/charge", 0.83, 0.09, 420),
        ("order-service", "POST /orders/confirm", 0.9, 0.01, 260),
    ];

    public override void Generate(SeedContext c)
    {
        for (var n = 0; n < c.PerHour(600); n++)
        {
            var traceId = Otlp.TraceId(c.Ids);
            var t = c.RandomTime(60);
            string? parent = null;
            foreach (var (service, span, reach, error, gapMs) in Steps)
            {
                if (c.Rng.NextDouble() > reach)
                {
                    break;
                }

                t += gapMs > 0 ? (long)(Math.Max(1, c.Gauss(gapMs, gapMs / 3 + 1)) * Otlp.NanosPerMs) : 0;
                var failed = c.Rng.NextDouble() < error;
                parent = c.Span(
                    c.Batch.Service(service), "Microsoft.AspNetCore", traceId, parent, span, KindServer, t, c.Rng.Next(20, 300),
                    Otlp.Attrs(("http.route", span.Split(' ')[1]), ("http.response.status_code", failed ? 502 : 200)),
                    error: failed ? "Bad Gateway" : null);
                if (failed)
                {
                    break;
                }
            }
        }
    }

    public override async Task CreateObjectsAsync(SeedContext c, CancellationToken ct)
    {
        var steps = new JsonArray(Steps.Select(s => (JsonNode)new JsonObject
        {
            ["serviceName"] = s.Service,
            ["spanName"] = s.Span,
            ["attributes"] = new JsonArray(),
        }).ToArray());
        var view = new JsonObject
        {
            ["name"] = c.T(ViewName),
            ["description"] = "",
            ["pageType"] = "Funnels",
            ["state"] = new JsonObject { ["steps"] = steps, ["windowPreset"] = c.Preset },
        };
        Console.WriteLine($"  saved funnel {await c.Api.CreateAsync(FlareApiClient.Kind.Views, view, ct)}");
    }
}

/// <summary>
/// Span trees with a structural choice in them - checkout → payment directly or via fraud-check,
/// api → redis (→ postgres on a miss) or straight to postgres - for the structural trace query
/// (<c>-&gt;</c> / <c>=&gt;</c>) docs. Its own service names (checkout, payment, api) keep it out
/// of the funnel's.
/// </summary>
public sealed class StructureScenario : Scenario
{
    public override string Name => "structure";

    public override string Description => "Traces that differ only by shape (fraud-check hop, cache hit/miss)";

    public override void Generate(SeedContext c)
    {
        const long ms = Otlp.NanosPerMs;
        JsonArray Route(string route) => Otlp.Attrs(("http.route", route));

        for (var n = 0; n < c.PerHour(270); n++)
        {
            var traceId = Otlp.TraceId(c.Ids);
            var t0 = c.RandomTime(60);
            var root = c.Span(c.Batch.Service("checkout"), "Microsoft.AspNetCore", traceId, null, "POST /checkout", KindServer, t0, c.Rng.NextDouble() * 650 + 250, Route("/checkout"));
            c.Span(c.Batch.Service("inventory"), "Microsoft.AspNetCore", traceId, root, "POST /inventory/reserve", KindServer, t0 + 8 * ms, c.Rng.NextDouble() * 45 + 15, Route("/inventory/reserve"));
            var failed = c.Rng.NextDouble() < 0.14 ? "card declined by issuer" : null;
            if (c.Rng.NextDouble() < 0.4)
            {
                var fraud = c.Span(c.Batch.Service("fraud-check"), "Microsoft.AspNetCore", traceId, root, "POST /fraud/score", KindServer, t0 + 80 * ms, c.Rng.NextDouble() * 180 + 120, Route("/fraud/score"));
                c.Span(c.Batch.Service("payment"), "Microsoft.AspNetCore", traceId, fraud, "POST /payments/charge", KindServer, t0 + 110 * ms, c.Rng.NextDouble() * 140 + 60, Route("/payments/charge"), error: failed);
            }
            else
            {
                c.Span(c.Batch.Service("payment"), "Microsoft.AspNetCore", traceId, root, "POST /payments/charge", KindServer, t0 + 90 * ms, c.Rng.NextDouble() * 140 + 60, Route("/payments/charge"), error: failed);
            }
        }

        for (var n = 0; n < c.PerHour(180); n++)
        {
            var traceId = Otlp.TraceId(c.Ids);
            var t0 = c.RandomTime(60);
            var root = c.Span(c.Batch.Service("api"), "Microsoft.AspNetCore", traceId, null, "GET /products/{id}", KindServer, t0, c.Rng.NextDouble() * 160 + 20, Route("/products/{id}"));
            if (c.Rng.NextDouble() < 0.75)
            {
                c.Span(c.Batch.Service("redis"), "StackExchange.Redis", traceId, root, "GET", KindClient, t0 + 2 * ms, c.Rng.NextDouble() * 1.7 + 0.3, Otlp.Attrs(("db.system", "redis")));
                if (c.Rng.NextDouble() < 0.3)
                {
                    c.Span(c.Batch.Service("postgres"), "Npgsql", traceId, root, "SELECT products", KindClient, t0 + 5 * ms, c.Rng.NextDouble() * 52 + 8, Otlp.Attrs(("db.system", "postgresql")));
                }
            }
            else
            {
                c.Span(c.Batch.Service("postgres"), "Npgsql", traceId, root, "SELECT products", KindClient, t0 + 3 * ms, c.Rng.NextDouble() * 52 + 8, Otlp.Attrs(("db.system", "postgresql")));
            }
        }
    }
}
