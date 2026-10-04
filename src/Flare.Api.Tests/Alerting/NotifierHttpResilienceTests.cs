using System.Net;
using Flare.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Xunit;

namespace Flare.Api.Tests.Alerting;

public sealed class NotifierHttpResilienceTests
{
    private sealed class Probe(HttpClient http)
    {
        public Task<HttpResponseMessage> PostAsync() => http.PostAsync("https://example.test/hook", new StringContent("{}"));
    }

    private sealed class ScriptedHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        public int Calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = statuses[Math.Min(Calls++, statuses.Length - 1)];
            var response = new HttpResponseMessage(status);
            if (status == HttpStatusCode.TooManyRequests)
            {
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.Zero);
            }

            return Task.FromResult(response);
        }
    }

    private static (Probe Probe, ScriptedHandler Handler) Build(params HttpStatusCode[] statuses)
    {
        var handler = new ScriptedHandler(statuses);
        var services = new ServiceCollection();
        // Same as AddServiceDefaults(): every client gets the standard handler.
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler(HttpRetryScope.Tune));
        services.AddHttpClient<Probe>().ConfigurePrimaryHttpMessageHandler(() => handler);
        services.Configure<HttpStandardResilienceOptions>("-standard", o => o.Retry.Delay = TimeSpan.FromMilliseconds(1));
        return (services.BuildServiceProvider().GetRequiredService<Probe>(), handler);
    }

    [Fact]
    public async Task Retries_429_then_succeeds()
    {
        var (probe, handler) = Build(HttpStatusCode.TooManyRequests, HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var response = await probe.PostAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task Send_test_is_single_shot()
    {
        var (probe, handler) = Build(HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        using var scope = HttpRetryScope.SingleShot();
        using var response = await probe.PostAsync();
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Client_error_is_not_retried()
    {
        var (probe, handler) = Build(HttpStatusCode.BadRequest, HttpStatusCode.OK);
        using var response = await probe.PostAsync();
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(1, handler.Calls);
    }
}
