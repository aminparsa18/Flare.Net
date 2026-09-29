using static ExampleApp.Seeder.SeedContext;

namespace ExampleApp.Seeder.Scenarios;

/// <summary>
/// HttpClient-shaped CLIENT spans to Stripe, Twilio, SendGrid, Slack, Google Maps and a
/// supplier API - 402/429/503s, timeouts with no status code, a Stripe latency spike and a
/// partner outage window - for the External APIs docs. Provider ids always contain digits, the
/// cue the page's endpoint templating uses to collapse <c>/v1/customers/cus_…</c> into
/// <c>/v1/customers/{id}</c>.
/// </summary>
public sealed class ExternalScenario : Scenario
{
    public override string Name => "external";

    public override string Description => "Third-party API calls with rate limits, declines, timeouts and an outage";

    private const string Alphanumeric = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    // Root span per calling service - different operations from the funnel scenario's, so the
    // two can be seeded together without one's roots joining the other's funnel steps.
    private static readonly Dictionary<string, string> Roots = new()
    {
        ["payment-service"] = "POST /payments/intents",
        ["notification-service"] = "POST /notifications",
        ["alerting-bot"] = "POST /alerts/dispatch",
        ["checkout-api"] = "POST /checkout/quote",
        ["storefront"] = "GET /stores/nearby",
    };

    public override void Generate(SeedContext c)
    {
        (string Host, string[] Callers, double PerHour, double P50, Func<(string Method, string Path)> Endpoint)[] hosts =
        [
            ("api.stripe.com", ["payment-service"], 1340, 240, () => c.Rng.NextDouble() switch
            {
                < 0.5 => ("POST", "/v1/payment_intents"),
                < 0.62 => ("POST", "/v1/customers"),
                < 0.82 => ("GET", $"/v1/customers/{Id(c, "cus_")}"),
                < 0.87 => ("POST", "/v1/refunds"),
                _ => ("GET", $"/v1/payment_intents/{Id(c, "pi_3Nx", 20)}"),
            }),
            ("api.twilio.com", ["notification-service"], 330, 180, () => ("POST", $"/2010-04-01/Accounts/AC{Hex(c, 16)}/Messages.json")),
            ("api.sendgrid.com", ["notification-service"], 435, 95, () => ("POST", "/v3/mail/send")),
            ("hooks.slack.com", ["alerting-bot"], 72, 130, () => ("POST", $"/services/T0{Id(c, "", 16).ToUpperInvariant()}/B0{Id(c, "", 16).ToUpperInvariant()}/{Id(c, "", 24)}")),
            ("maps.googleapis.com", ["checkout-api", "storefront"], 880, 60, () => c.Rng.NextDouble() < 0.5 ? ("GET", "/maps/api/geocode/json") : ("GET", "/maps/api/distancematrix/json")),
            ("inventory.partner-corp.com", ["checkout-api"], 540, 110, () => c.Rng.NextDouble() < 0.5 ? ("GET", $"/v2/stock/{c.Rng.Next(100000, 999999)}") : ("POST", "/v2/reservations")),
        ];

        foreach (var (host, callers, perHour, p50, endpoint) in hosts)
        {
            for (var n = 0; n < c.PerHour(perHour); n++)
            {
                var service = c.Pick(callers);
                var t0 = c.RandomTime(30);
                var (code, errorType, message, multiplier) = Outcome(c, host, c.Offset(t0) / (c.WindowMinutes * 60.0));
                var durationMs = c.LogNormal(p50 * multiplier, 0.35);
                var (method, path) = endpoint();
                var resource = c.Batch.Service(service);
                var traceId = Otlp.TraceId(c.Ids);

                var clientAttributes = Otlp.Attrs(("server.address", host), ("server.port", 443), ("http.request.method", method), ("url.full", $"https://{host}{path}"));
                if (code is { } status)
                {
                    clientAttributes.Add(Otlp.Attr("http.response.status_code", status));
                }

                if (errorType is not null)
                {
                    clientAttributes.Add(Otlp.Attr("error.type", errorType));
                }

                var root = c.Span(resource, "Microsoft.AspNetCore", traceId, null, Roots[service], KindServer, t0, durationMs + 25,
                    Otlp.Attrs(("http.route", Roots[service].Split(' ')[1])), error: errorType is null ? null : "Upstream call failed");
                c.Span(resource, "System.Net.Http", traceId, root, method, KindClient, t0 + 5 * Otlp.NanosPerMs, durationMs,
                    clientAttributes, error: errorType is null ? null : message, okStatus: 0);
            }
        }
    }

    /// <summary>(status code or none, error.type or none, status message, latency multiplier) at <paramref name="progress"/> (0-1) through the window.</summary>
    private static (int? Code, string? ErrorType, string? Message, double Multiplier) Outcome(SeedContext c, string host, double progress)
    {
        var roll = c.Rng.NextDouble();
        switch (host)
        {
            case "api.stripe.com" when progress is > 0.43 and < 0.52:
                return (200, null, null, 3.5); // latency spike
            case "api.stripe.com" when roll < 0.05:
                return (402, "402", "card_declined", 1);
            case "api.stripe.com" when roll < 0.065:
                return (429, "429", "rate_limit", 1);
            case "api.stripe.com" when roll < 0.075:
                return (null, "System.Threading.Tasks.TaskCanceledException", "The request was canceled due to the configured HttpClient.Timeout of 10 seconds elapsing.", 40);
            case "api.stripe.com":
                return (roll < 0.3 ? 201 : 200, null, null, 1);
            case "api.twilio.com" when roll < 0.04:
                return (400, "400", "21211: invalid 'To' phone number", 1);
            case "api.sendgrid.com":
                return (202, null, null, 1);
            case "inventory.partner-corp.com" when progress is > 0.69 and < 0.78 && roll < 0.7:
                return (503, "503", "Service Unavailable", 1.5);
            case "maps.googleapis.com" when roll < 0.01:
                return (429, "429", "OVER_QUERY_LIMIT", 1);
            case "hooks.slack.com" when roll < 0.03:
                return (null, "System.Net.Http.HttpRequestException", "Connection refused (hooks.slack.com:443)", 1);
            default:
                return (200, null, null, 1);
        }
    }

    private static string Id(SeedContext c, string prefix, int length = 14)
    {
        var chars = Enumerable.Range(0, length - 2).Select(_ => Alphanumeric[c.Rng.Next(Alphanumeric.Length)])
            .Concat(Enumerable.Range(0, 2).Select(_ => (char)('0' + c.Rng.Next(10))))
            .ToArray();
        c.Rng.Shuffle(chars);
        return prefix + new string(chars);
    }

    private static string Hex(SeedContext c, int bytes)
    {
        var buffer = new byte[bytes];
        c.Rng.NextBytes(buffer);
        return Convert.ToHexStringLower(buffer);
    }
}
