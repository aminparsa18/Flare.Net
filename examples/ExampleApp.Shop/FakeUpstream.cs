using System.Security.Cryptography;

namespace ExampleApp.Shop;

/// <summary>
/// Stands in for every third-party API the shop calls. <see cref="ExternalApiClient"/> connects
/// here whatever host a URL names; this answers per <c>Host</c> header with each provider's
/// usual latency and failure mix - a WireMock-style stub, minus the extra container.
/// </summary>
/// <remarks>
/// Failure shapes, and why each exists:
/// <list type="bullet">
/// <item>api.stripe.com: 402 card_declined, 429 rate_limit, and a hang past the client's 5 s
/// timeout (a timeout with no status code). 4x latency under <see cref="ScenarioFlags.LatencySpike"/>.</item>
/// <item>inventory.partner-corp.com: 503 for most calls under <see cref="ScenarioFlags.PartnerOutage"/>.</item>
/// <item>hooks.slack.com: the connection is dropped mid-request - an HttpRequestException, no status code.</item>
/// <item>api.twilio.com 400 (bad phone number), maps.googleapis.com 429 (OVER_QUERY_LIMIT).</item>
/// </list>
/// </remarks>
public static class FakeUpstream
{
    public static void Map(WebApplication app) =>
        app.Map("/{**path}", async (HttpContext context, ScenarioState scenario) =>
        {
            var flags = scenario.Current;
            var host = context.Request.Host.Host.ToLowerInvariant();
            var roll = Random.Shared.NextDouble();

            // p50 latency per provider, roughly what each one's status page quotes.
            var p50Ms = host switch
            {
                "api.stripe.com" => 240,
                "api.twilio.com" => 180,
                "api.sendgrid.com" => 95,
                "hooks.slack.com" => 130,
                "maps.googleapis.com" => 60,
                "inventory.partner-corp.com" => 110,
                _ => 50,
            };
            var multiplier = host switch
            {
                "api.stripe.com" when flags.LatencySpike => 4.0,
                "inventory.partner-corp.com" when flags.PartnerOutage => 1.5,
                _ => 1.0,
            };

            if (host == "api.stripe.com" && roll < 0.008)
            {
                // Longer than ExternalApiClient's 5 s timeout: the caller gives up first.
                await Task.Delay(TimeSpan.FromSeconds(8), context.RequestAborted);
                return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
            }

            await Task.Delay(Latency.Sample(p50Ms * multiplier), context.RequestAborted);

            switch (host)
            {
                case "api.stripe.com" when roll < 0.058:
                    return Results.Json(new { error = new { type = "card_error", code = "card_declined", message = "Your card was declined." } }, statusCode: 402);
                case "api.stripe.com" when roll < 0.073:
                    return Results.Json(new { error = new { type = "rate_limit_error", message = "Too many requests." } }, statusCode: 429);
                case "api.stripe.com":
                    return Results.Json(new { id = "pi_3Nx" + RandomNumberGenerator.GetHexString(20, lowercase: true), status = "succeeded" });
                case "api.twilio.com" when roll < 0.04:
                    return Results.Json(new { code = 21211, message = "The 'To' number is not a valid phone number." }, statusCode: 400);
                case "api.twilio.com":
                    return Results.Json(new { sid = "SM" + RandomNumberGenerator.GetHexString(32, lowercase: true), status = "queued" }, statusCode: 201);
                case "api.sendgrid.com":
                    return Results.StatusCode(StatusCodes.Status202Accepted);
                case "hooks.slack.com" when roll < 0.03:
                    context.Abort();
                    return Results.Empty;
                case "hooks.slack.com":
                    return Results.Text("ok");
                case "maps.googleapis.com" when roll < 0.01:
                    return Results.Json(new { status = "OVER_QUERY_LIMIT" }, statusCode: 429);
                case "maps.googleapis.com":
                    return Results.Json(new { status = "OK", results = new[] { new { place_id = "ChIJ" + RandomNumberGenerator.GetHexString(16, lowercase: true) } } });
                case "inventory.partner-corp.com" when flags.PartnerOutage && roll < 0.7:
                    return Results.Json(new { error = "upstream warehouse system unavailable" }, statusCode: 503);
                case "inventory.partner-corp.com":
                    return Results.Json(new { available = Random.Shared.Next(0, 400), warehouse = "partner-eu-2" });
                default:
                    return Results.NotFound();
            }
        });
}
