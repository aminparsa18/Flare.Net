using System.Text;
using Flare.Ingest.Otlp;
using Flare.Ingest.Sinks;
using Flare.Ingest.Stats;
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
/// Reports through <see cref="IIngestionStatsTracker"/> under the dedicated
/// <see cref="IngestionProtocol.Scrape"/> value (added alongside this worker's Ingestion-
/// page stats/UI follow-up, Planning.md v20's deferred item) - a third, pull-based
/// protocol distinct from <see cref="IngestionProtocol.Grpc"/>/<see cref="IngestionProtocol.Http"/>,
/// not folded into either, so scrape activity gets its own row instead of silently
/// inflating the real "HTTP :4318" receiver counters.
/// </remarks>
public sealed class PrometheusScrapeWorker(
    IOptions<PrometheusScrapeOptions> options,
    IHttpClientFactory httpClientFactory,
    IMetricEventSink sink,
    IIngestionStatsTracker stats,
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
                await stats.RecordRejectedAsync(
                    IngestionSignal.Metrics, IngestionProtocol.Scrape, $"scrape-status:{(int)response.StatusCode}", stoppingToken);
                return;
            }

            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            var byteCount = Encoding.UTF8.GetByteCount(body);

            var parsed = PrometheusExpositionParser.Parse(body);
            // Also becomes every mapped point's IngestedAt - see PrometheusMetricsMapper's
            // remarks for why "scrape time" and "receipt time" are the same moment here,
            // unlike OTLP's push model.
            var scrapeTime = timeProvider.GetUtcNow();
            var result = PrometheusMetricsMapper.Map(parsed, target, scrapeTime);

            foreach (var point in result.Points)
            {
                await sink.WriteAsync(point, stoppingToken);
            }

            await stats.RecordAcceptedAsync(IngestionSignal.Metrics, IngestionProtocol.Scrape, result.Points.Count, byteCount, stoppingToken);
            await stats.RecordServiceBreakdownAsync(
                IngestionSignal.Metrics,
                ServiceBreakdown.Build(result.Points.Select(p => (p.ServiceName, ClockSkew.Nanos(scrapeTime, p.Time))), byteCount),
                stoppingToken);

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
            await stats.RecordRejectedAsync(IngestionSignal.Metrics, IngestionProtocol.Scrape, $"scrape-failed:{ex.GetType().Name}", stoppingToken);
        }
    }
}
