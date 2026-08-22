using Flare.Ingest.Sinks;
using Microsoft.Extensions.Options;

namespace Flare.Ingest.Prometheus;

/// <summary>
/// The pull-side counterpart to <see cref="Otlp.OtlpHttpMetricsEndpoints"/>/
/// <see cref="Otlp.OtlpGrpcMetricsService"/>: periodically scrapes each configured
/// <see cref="PrometheusScrapeTargetOptions"/>'s <c>/metrics</c> endpoint and feeds the
/// parsed points into the same <see cref="IMetricEventSink"/> the OTLP receivers use, so
/// scraped metrics flow through the exact same Redis-stream/<see cref="Pipeline.MetricFlushWorker"/>/
/// ClickHouse pipeline as pushed ones - this is the only new piece, not a parallel storage
/// path.
/// </summary>
/// <remarks>
/// No-ops immediately if <see cref="PrometheusScrapeOptions.Targets"/> is empty (the
/// default) - see <see cref="PrometheusScrapeOptions"/>'s remarks for why that's the only
/// on/off switch. Runs one independent <see cref="PeriodicTimer"/> loop per target (each on
/// its own <see cref="PrometheusScrapeTargetOptions.Interval"/>) under one
/// <see cref="Task.WhenAll(IEnumerable{Task})"/> for the life of <see cref="ExecuteAsync"/>,
/// so one slow or down target never delays another's cadence - same "independent per-unit
/// loops, one shared lifetime" shape <see cref="MetricFlushWorker"/> and
/// <see cref="Pipeline.SpanFlushWorker"/> already use for their own poll loops, just
/// fanned out per target instead of per consumer group.
///
/// Deliberately does not call <see cref="Stats.IIngestionStatsTracker"/> - v1 scope is
/// backend-only (see Planning.md's v20 entry): those Redis-backed minute-bucket counters
/// are the exact "Http protocol on port 4318" counters the dashboard's Ingestion page
/// already renders, and tagging scrape activity onto the existing <c>Http</c> enum value
/// would silently corrupt that count rather than just leave scrape activity unreported.
/// Plain <see cref="ILogger"/> is the whole observability story here for now.
/// </remarks>
public sealed class PrometheusScrapeWorker(
    IOptions<PrometheusScrapeOptions> options,
    IHttpClientFactory httpClientFactory,
    IMetricEventSink sink,
    TimeProvider timeProvider,
    ILogger<PrometheusScrapeWorker> logger) : BackgroundService
{
    private const string HttpClientName = "PrometheusScrape";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var targets = options.Value.Targets;
        if (targets.Count == 0)
        {
            logger.LogDebug("Prometheus scrape disabled - no targets configured");
            return;
        }

        logger.LogInformation("Starting Prometheus scrape for {Count} target(s)", targets.Count);

        var loops = targets.Select(target => RunTargetLoopAsync(target, stoppingToken));
        await Task.WhenAll(loops);
    }

    private async Task RunTargetLoopAsync(PrometheusScrapeTargetOptions target, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(target.Interval);

        // Scrape once immediately rather than waiting a full interval for the first tick,
        // then fall in line with the timer - same "don't make the operator wait" instinct
        // as IngestApiKeyCache's own pre-traffic InitializeAsync call.
        await ScrapeOnceAsync(target, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    break;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await ScrapeOnceAsync(target, stoppingToken);
        }
    }

    private async Task ScrapeOnceAsync(PrometheusScrapeTargetOptions target, CancellationToken stoppingToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeoutCts.CancelAfter(target.Timeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, target.Url);
            foreach (var (key, value) in target.Headers)
            {
                request.Headers.TryAddWithoutValidation(key, value);
            }

            var client = httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Prometheus scrape of {Job} ({Url}) failed with status {StatusCode}",
                    target.Job, target.Url, (int)response.StatusCode);
                return;
            }

            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            var parsed = PrometheusExpositionParser.Parse(body);
            var result = PrometheusMetricsMapper.Map(parsed, target, timeProvider.GetUtcNow());

            foreach (var point in result.Points)
            {
                await sink.WriteAsync(point, stoppingToken);
            }

            if (result.UnsupportedMetricNames.Count > 0)
            {
                logger.LogWarning(
                    "Prometheus scrape of {Job} dropped {Count} metric(s) with an unsupported shape (Summary/malformed histogram not supported): {Names}",
                    target.Job, result.UnsupportedMetricNames.Count, string.Join(", ", result.UnsupportedMetricNames));
            }

            logger.LogDebug("Scraped {Count} metric data point(s) from {Job} ({Url})", result.Points.Count, target.Job, target.Url);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Prometheus scrape of {Job} ({Url}) failed", target.Job, target.Url);
        }
    }
}
