using System.Net.Sockets;
using System.Security.Cryptography;

namespace ExampleApp.Shop;

/// <summary>
/// The shop's calls to third-party APIs (Stripe, Twilio, SendGrid, Slack, Google Maps, a
/// supplier's stock API) - real <see cref="HttpClient"/> requests to the real host names, so
/// the HttpClient instrumentation records <c>server.address=api.stripe.com</c>,
/// <c>url.full=http://api.stripe.com/v1/...</c> and so on, exactly as a production app would.
/// </summary>
/// <remarks>
/// <para>
/// Nothing leaves the machine: the named client's <see cref="SocketsHttpHandler.ConnectCallback"/>
/// opens every connection to the <c>fake-upstream</c> resource instead, whatever host the URL
/// names, and <see cref="FakeUpstream"/> answers per <c>Host</c> header. The shop services run as
/// host processes, not containers, so a container network alias couldn't give them these
/// names - redirecting at connect time is the host-process equivalent of pointing DNS at a stub.
/// </para>
/// <para>
/// Plain <c>http://</c> (port 80) rather than <c>https://</c>: the stub has no certificate for
/// api.stripe.com, and faking one would mean turning certificate validation off.
/// </para>
/// <para>
/// ServiceDefaults' standard resilience handler is removed for this client - its retries would
/// hide the upstream's 429/503s behind a successful third attempt, and its 10 s attempt timeout
/// is longer than this client's own. A plain 5 s <see cref="HttpClient.Timeout"/> instead, so
/// a hung upstream call surfaces as a <see cref="TaskCanceledException"/> with no status code
/// - the External APIs page's "timeouts" case.
/// </para>
/// </remarks>
public sealed class ExternalApiClient(HttpClient http)
{
    public const string Name = "external";

    public Task<HttpResponseMessage> StripeCreatePaymentIntentAsync(decimal amount, string currency, CancellationToken ct) =>
        http.PostAsJsonAsync("http://api.stripe.com/v1/payment_intents", new { amount = (long)(amount * 100), currency }, ct);

    public Task<HttpResponseMessage> StripeGetCustomerAsync(CancellationToken ct) =>
        http.GetAsync($"http://api.stripe.com/v1/customers/{Ids.Stripe("cus_")}", ct);

    public Task<HttpResponseMessage> TwilioSendSmsAsync(string to, string body, CancellationToken ct) =>
        http.PostAsync(
            $"http://api.twilio.com/2010-04-01/Accounts/{Ids.TwilioAccount}/Messages.json",
            new FormUrlEncodedContent([new("To", to), new("From", "+15005550006"), new("Body", body)]), ct);

    public Task<HttpResponseMessage> SendGridSendAsync(string to, string subject, CancellationToken ct) =>
        http.PostAsJsonAsync("http://api.sendgrid.com/v3/mail/send", new { personalizations = new[] { new { to = new[] { new { email = to } } } }, subject }, ct);

    public Task<HttpResponseMessage> SlackPostAsync(string text, CancellationToken ct) =>
        http.PostAsJsonAsync($"http://hooks.slack.com/services/{Ids.SlackWebhook}", new { text }, ct);

    public Task<HttpResponseMessage> MapsGeocodeAsync(string address, CancellationToken ct) =>
        http.GetAsync($"http://maps.googleapis.com/maps/api/geocode/json?address={Uri.EscapeDataString(address)}", ct);

    public Task<HttpResponseMessage> MapsDistanceAsync(CancellationToken ct) =>
        http.GetAsync("http://maps.googleapis.com/maps/api/distancematrix/json?origins=warehouse-3&destinations=customer", ct);

    public Task<HttpResponseMessage> PartnerStockAsync(int partnerSku, CancellationToken ct) =>
        http.GetAsync($"http://inventory.partner-corp.com/v2/stock/{partnerSku}", ct);

    public Task<HttpResponseMessage> PartnerReserveAsync(int partnerSku, int quantity, CancellationToken ct) =>
        http.PostAsJsonAsync("http://inventory.partner-corp.com/v2/reservations", new { sku = partnerSku, quantity }, ct);

    /// <summary>
    /// Ids shaped like the real providers' - always with digits in them, which is what the
    /// External APIs page's endpoint templating keys on to collapse <c>/v1/customers/cus_...</c>
    /// into <c>/v1/customers/{id}</c>.
    /// </summary>
    private static class Ids
    {
        private const string Alphanumeric = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

        // One account and one webhook per shop, like a real deployment's config.
        public static readonly string TwilioAccount = "AC" + RandomNumberGenerator.GetHexString(32, lowercase: true);
        public static readonly string SlackWebhook = $"T0{Upper(9)}/B0{Upper(9)}/{Stripe("", 24)}";

        public static string Stripe(string prefix, int length = 14)
        {
            var chars = Random.Shared.GetItems(Alphanumeric.AsSpan(), length - 2).Concat(Random.Shared.GetItems("0123456789".AsSpan(), 2)).ToArray();
            Random.Shared.Shuffle(chars);
            return prefix + new string(chars);
        }

        private static string Upper(int length) =>
            new(Random.Shared.GetItems("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".AsSpan(), length - 1).Append('7').ToArray());
    }
}

public static class ExternalApiClientExtensions
{
    public static IServiceCollection AddExternalApiClient(this IServiceCollection services)
    {
        // RemoveAllResilienceHandlers is still marked experimental (EXTEXP0001) in
        // Microsoft.Extensions.Http.Resilience - it's the documented way to opt one client out
        // of ConfigureHttpClientDefaults' AddStandardResilienceHandler.
#pragma warning disable EXTEXP0001
        services.AddHttpClient<ExternalApiClient>(ExternalApiClient.Name, client => client.Timeout = TimeSpan.FromSeconds(5))
            .RemoveAllResilienceHandlers()
#pragma warning restore EXTEXP0001
            .ConfigurePrimaryHttpMessageHandler(sp =>
            {
                var configuration = sp.GetRequiredService<IConfiguration>();
                return new SocketsHttpHandler
                {
                    ConnectCallback = async (_, cancellationToken) =>
                    {
                        // Resolved per connection rather than once, so a service that doesn't
                        // reference fake-upstream only fails if it actually calls out.
                        var upstream = configuration["services:fake-upstream:http:0"]
                            ?? throw new InvalidOperationException("No fake-upstream endpoint configured - reference it from the AppHost (.WithReference(fakeUpstream)).");
                        var target = new Uri(upstream);
                        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                        try
                        {
                            await socket.ConnectAsync(target.Host, target.Port, cancellationToken);
                            return new NetworkStream(socket, ownsSocket: true);
                        }
                        catch
                        {
                            socket.Dispose();
                            throw;
                        }
                    },
                };
            });

        return services;
    }
}
