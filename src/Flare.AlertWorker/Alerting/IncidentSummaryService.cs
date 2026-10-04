using Flare.Api.Ai;
using Flare.Api.Alerting;
using Flare.Api.Model;
using Flare.Api.Query;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.AlertWorker.Alerting;

/// <summary>Queues an AI incident summary for a fired alert. Returns immediately; never throws.</summary>
public interface IIncidentSummaryService
{
    /// <param name="channels">The channels the plain notification went to - the summary follows it to the eligible ones.</param>
    void Enqueue(AlertRule rule, AlertHistoryEntry fired, IReadOnlyList<NotificationChannel> channels, string? metricUnit, AnomalyScore? anomaly);
}

/// <summary>
/// AI incident summaries (ADR-0104). Runs strictly after the plain notification and history row
/// are written, on its own bounded background task, so a slow or failing model can never delay or
/// lose an alert. Off unless <c>Ai__Enabled</c> and <c>Ai__IncidentSummaries</c> are set.
/// </summary>
/// <remarks>
/// Bounded three ways: at most <see cref="MaxConcurrent"/> summaries in flight (excess is dropped,
/// not queued), at most <see cref="AiOptions.IncidentSummariesPerHour"/> per clock hour (a Redis
/// counter, since the tick lock moves between replicas), and the prompt/answer caps in
/// <see cref="AiOptions"/>.
/// </remarks>
public sealed class IncidentSummaryService(
    IAlertQueryService alerts,
    IAlertEvidenceQueryService evidenceQueries,
    ILlmClient llm,
    CompositeAlertNotifier notifier,
    IConnectionMultiplexer redis,
    IOptionsMonitor<AiOptions> options,
    IHostApplicationLifetime lifetime,
    TimeProvider timeProvider,
    ILogger<IncidentSummaryService> logger) : IIncidentSummaryService
{
    private const int MaxConcurrent = 2;
    private const int PatternLimit = 8;
    private const int ExceptionLimit = 5;
    private const int SpanLimit = 10;

    private static readonly IReadOnlyList<byte> ErrorSeverities = [17, 18, 19, 20, 21, 22, 23, 24];

    private readonly SemaphoreSlim slots = new(MaxConcurrent, MaxConcurrent);

    public void Enqueue(AlertRule rule, AlertHistoryEntry fired, IReadOnlyList<NotificationChannel> channels, string? metricUnit, AnomalyScore? anomaly)
    {
        if (!options.CurrentValue.IncidentSummariesActive || fired.Resolved || channels.Count == 0)
        {
            return;
        }

        if (!slots.Wait(0))
        {
            logger.LogInformation("AI incident summary for rule {RuleId} skipped: {Max} already in flight.", rule.Id, MaxConcurrent);
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await SummarizeAsync(rule, fired, channels, metricUnit, anomaly, lifetime.ApplicationStopping);
            }
            catch (OperationCanceledException) when (lifetime.ApplicationStopping.IsCancellationRequested)
            {
                // Shutting down.
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "AI incident summary for rule {RuleId} failed.", rule.Id);
            }
            finally
            {
                slots.Release();
            }
        });
    }

    private async Task SummarizeAsync(AlertRule rule, AlertHistoryEntry fired, IReadOnlyList<NotificationChannel> channels, string? metricUnit, AnomalyScore? anomaly, CancellationToken cancellationToken)
    {
        var config = options.CurrentValue;
        if (!await TryTakeBudgetAsync(config.IncidentSummariesPerHour))
        {
            logger.LogInformation("AI incident summary for rule {RuleId} skipped: hourly cap of {Cap} reached.", rule.Id, config.IncidentSummariesPerHour);
            return;
        }

        var evidence = await GatherAsync(rule, fired, metricUnit, anomaly, cancellationToken);
        var prompt = IncidentSummaryPromptBuilder.Build(evidence, config.MaxInputChars);
        logger.LogInformation(
            "AI incident summary for rule {RuleId} ({RuleName}): sending {Chars} redacted chars to model {Model}",
            rule.Id, rule.Name, prompt.Length, llm.Model);
        logger.LogDebug("AI incident summary prompt: {Prompt}", prompt);

        var (summary, error) = await llm.CompleteAsync(IncidentSummaryPromptBuilder.SystemPrompt, prompt, cancellationToken);
        if (summary is null)
        {
            logger.LogWarning("AI incident summary for rule {RuleId} not generated: {Error}", rule.Id, error);
            return;
        }

        await alerts.InsertEventSummaryAsync(fired.EventId, rule.Id, llm.Model, summary, prompt, timeProvider.GetUtcNow(), cancellationToken);

        var eligible = channels.Where(IncidentSummaryFollowUp.IsEligible).ToList();
        if (eligible.Count > 0)
        {
            var results = await notifier.SendAllAsync(
                IncidentSummaryFollowUp.BuildRule(rule, summary), eligible, evidence.Observed, fired.FiredAt, cancellationToken, metricUnit: metricUnit);
            foreach (var (channel, result) in eligible.Zip(results))
            {
                if (!result.Success)
                {
                    logger.LogWarning("AI summary follow-up to channel {Channel} failed: {Error}", channel.Name, result.Error);
                }
            }
        }
    }

    /// <summary>One counter per clock hour in Redis, because the evaluation lock moves between replicas. Fails closed: if the budget can't be read, no model call is made.</summary>
    private async Task<bool> TryTakeBudgetAsync(int perHour)
    {
        try
        {
            var db = redis.GetDatabase();
            var key = (RedisKey)$"flare:ai:incident-summaries:{timeProvider.GetUtcNow().ToUnixTimeSeconds() / 3600}";
            var count = await db.StringIncrementAsync(key);
            if (count == 1)
            {
                await db.KeyExpireAsync(key, TimeSpan.FromHours(2));
            }

            return count <= perHour;
        }
        catch (Exception ex) when (ex is RedisException)
        {
            logger.LogWarning(ex, "Could not read the AI incident summary budget; skipping.");
            return false;
        }
    }

    private async Task<IncidentEvidence> GatherAsync(AlertRule rule, AlertHistoryEntry fired, string? metricUnit, AnomalyScore? anomaly, CancellationToken cancellationToken)
    {
        var window = TimeSpan.FromSeconds(fired.WindowSeconds);
        var to = fired.FiredAt;
        var from = to - window;
        var observed = fired.ObservedValue ?? fired.ObservedCount;
        var kind = AnomalyScoring.SeriesKind(rule.ConditionKind, rule.AnomalyCondition);

        var evidence = new IncidentEvidence
        {
            Rule = rule,
            From = from,
            To = to,
            NoData = fired.NoData,
            Observed = observed,
            MetricUnit = metricUnit,
            Anomaly = anomaly,
        };
        if (fired.NoData)
        {
            return evidence;
        }

        // Each piece of evidence is best-effort: a failed or slow query drops that section, not the summary.
        var previous = rule.ConditionKind == AlertConditionKind.Anomaly ? null : await TryAsync(() => PreviousAsync(rule, kind, from, window, cancellationToken));
        var exceptions = kind == AlertConditionKind.ExceptionCount && rule.ExceptionCondition is { } exceptionCondition
            ? await TryAsync(() => evidenceQueries.GetExceptionGroupsAsync(exceptionCondition, from, to, ExceptionLimit, cancellationToken))
            : null;
        var patterns = await TryAsync(() => evidenceQueries.GetLogPatternsAsync(LogScope(rule, kind), from, to, PatternLimit, cancellationToken));

        var traceId = exceptions?.Select(e => e.TraceId).FirstOrDefault(id => id.Length > 0)
            ?? patterns?.Select(p => p.TraceId).FirstOrDefault(id => id.Length > 0);
        var spans = traceId is null ? null : await TryAsync(() => evidenceQueries.GetErrorSpansAsync(traceId, from, to, SpanLimit, cancellationToken));

        return evidence with
        {
            Previous = previous,
            Exceptions = exceptions ?? [],
            LogPatterns = patterns ?? [],
            ErrorSpans = spans ?? [],
        };
    }

    /// <summary>A log rule's own condition; for any other rule, the error-or-worse logs of the services it is scoped to.</summary>
    private static LogFilter LogScope(AlertRule rule, AlertConditionKind kind)
    {
        if (kind == AlertConditionKind.LogCount)
        {
            return rule.Condition;
        }

        var services = kind switch
        {
            AlertConditionKind.MetricThreshold => rule.MetricCondition?.Filter.Services,
            AlertConditionKind.ExceptionCount => rule.ExceptionCondition?.Filter.Services,
            _ => null,
        };
        return new LogFilter { Services = services, SeverityNumbers = ErrorSeverities };
    }

    private async Task<double?> PreviousAsync(AlertRule rule, AlertConditionKind kind, DateTimeOffset from, TimeSpan window, CancellationToken cancellationToken)
    {
        var previousFrom = from - window;
        switch (kind)
        {
            case AlertConditionKind.MetricThreshold when rule.MetricCondition is not null:
                var (value, _) = await alerts.EvaluateMetricConditionAsync(rule.MetricCondition, previousFrom, from, cancellationToken);
                return double.IsNaN(value) ? null : value;
            case AlertConditionKind.ExceptionCount when rule.ExceptionCondition is not null:
                return await alerts.CountMatchingExceptionsAsync(rule.ExceptionCondition, previousFrom, from, cancellationToken);
            case AlertConditionKind.LogCount:
                return await alerts.CountMatchingLogsAsync(rule.Condition, previousFrom, from, cancellationToken);
            default:
                return null;
        }
    }

    private async Task<T?> TryAsync<T>(Func<Task<T>> query)
    {
        try
        {
            return await query();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "AI incident summary evidence query failed; continuing without it.");
            return default;
        }
    }
}
