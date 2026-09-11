using System.Text.Json;
using Flare.Api.Alerting;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>The Alerting API: rule CRUD, fired-alert history, and evaluation dry-runs under <c>/api/alerts</c>.</summary>
/// <remarks>
/// Mirrors <see cref="LogsEndpoints"/>'s shape: POST/PUT + JSON body for anything with a
/// structured payload, manual <see cref="JsonSerializer.DeserializeAsync"/> against the
/// source-gen <see cref="AlertsJsonContext"/>, <see cref="Results.Problem"/> on 400s.
/// </remarks>
public static class AlertEndpoints
{
    public static IEndpointRouteBuilder MapAlertEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/alerts", HandleCreateAsync);
        endpoints.MapGet("/api/alerts", HandleListAsync);
        endpoints.MapGet("/api/alerts/{id:guid}", HandleGetAsync);
        endpoints.MapPut("/api/alerts/{id:guid}", HandleUpdateAsync);
        endpoints.MapDelete("/api/alerts/{id:guid}", HandleDeleteAsync);
        endpoints.MapGet("/api/alerts/{id:guid}/history", HandleHistoryAsync);
        // Saved-rule dry-run first (more specific route) so it doesn't get shadowed by
        // the draft-rule route below.
        endpoints.MapPost("/api/alerts/{id:guid}/test", HandleTestSavedAsync);
        endpoints.MapPost("/api/alerts/test", HandleTestDraftAsync);
        // "Send test alert": actually notifies through the configured channel (unlike the
        // dry-runs above, which only evaluate the condition) - saved-rule route first
        // (more specific) so it isn't shadowed by the draft route below, same ordering
        // reason as the dry-run pair.
        endpoints.MapPost("/api/alerts/{id:guid}/send-test", HandleSendTestSavedAsync);
        endpoints.MapPost("/api/alerts/send-test", HandleSendTestDraftAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        AlertRuleRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AlertsJsonContext.Default.AlertRuleRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ValidateChannel() is { } channelError)
        {
            return Results.Problem(channelError, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ValidateCondition() is { } conditionError)
        {
            return Results.Problem(conditionError, statusCode: StatusCodes.Status400BadRequest);
        }

        var rule = await alerts.CreateAsync(request, cancellationToken);
        return ApiSerialization.Write(http, rule, AlertsJsonContext.Default.AlertRule, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAsync(HttpContext http, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        var rules = await alerts.ListAsync(cancellationToken);
        return ApiSerialization.Write(http, new AlertRuleListResponse { Rules = rules }, AlertsJsonContext.Default.AlertRuleListResponse);
    }

    private static async Task<IResult> HandleGetAsync(Guid id, HttpContext http, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        var rule = await alerts.GetAsync(id, cancellationToken);
        return rule is null ? Results.NotFound() : ApiSerialization.Write(http, rule, AlertsJsonContext.Default.AlertRule);
    }

    private static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        AlertRuleRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AlertsJsonContext.Default.AlertRuleRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ValidateChannel() is { } channelError)
        {
            return Results.Problem(channelError, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ValidateCondition() is { } conditionError)
        {
            return Results.Problem(conditionError, statusCode: StatusCodes.Status400BadRequest);
        }

        var rule = await alerts.UpdateAsync(id, request, cancellationToken);
        return rule is null ? Results.NotFound() : ApiSerialization.Write(http, rule, AlertsJsonContext.Default.AlertRule);
    }

    private static async Task<IResult> HandleDeleteAsync(Guid id, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        var deleted = await alerts.DeleteAsync(id, cancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> HandleHistoryAsync(Guid id, int? limit, HttpContext http, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        var events = await alerts.GetHistoryAsync(id, limit is > 0 ? limit.Value : 50, cancellationToken);
        return ApiSerialization.Write(http, new AlertHistoryResponse { Events = events }, AlertsJsonContext.Default.AlertHistoryResponse);
    }

    private static async Task<IResult> HandleTestSavedAsync(Guid id, HttpContext http, IAlertQueryService alerts, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var rule = await alerts.GetAsync(id, cancellationToken);
        if (rule is null)
        {
            return Results.NotFound();
        }

        var result = await EvaluateAsync(alerts, timeProvider, rule.ConditionKind, rule.Condition, rule.Threshold, rule.MetricCondition, rule.MetricThresholdValue, rule.WindowSeconds, cancellationToken);
        return ApiSerialization.Write(http, result, AlertsJsonContext.Default.AlertTestResult);
    }

    private static async Task<IResult> HandleTestDraftAsync(HttpContext http, IAlertQueryService alerts, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        AlertRuleRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AlertsJsonContext.Default.AlertRuleRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await EvaluateAsync(alerts, timeProvider, request.ConditionKind ?? AlertConditionKind.LogCount, request.Condition, request.Threshold, request.MetricCondition, request.MetricThresholdValue, request.WindowSeconds, cancellationToken);
        return ApiSerialization.Write(http, result, AlertsJsonContext.Default.AlertTestResult);
    }

    private static async Task<IResult> HandleSendTestSavedAsync(Guid id, HttpContext http, IAlertQueryService alerts, IAlertNotifier notifier, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var rule = await alerts.GetAsync(id, cancellationToken);
        if (rule is null)
        {
            return Results.NotFound();
        }

        var result = await SendTestAsync(notifier, rule, timeProvider, cancellationToken);
        return ApiSerialization.Write(http, result, AlertsJsonContext.Default.AlertNotificationTestResult);
    }

    private static async Task<IResult> HandleSendTestDraftAsync(HttpContext http, IAlertNotifier notifier, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        AlertRuleRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AlertsJsonContext.Default.AlertRuleRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        // Unlike the dry-run draft test above, this one actually notifies - so it needs
        // the same channel validation as create/update, not just an evaluable condition.
        if (request.ValidateChannel() is { } channelError)
        {
            return Results.Problem(channelError, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.ValidateCondition() is { } conditionError)
        {
            return Results.Problem(conditionError, statusCode: StatusCodes.Status400BadRequest);
        }

        var now = timeProvider.GetUtcNow();
        var defaults = AlertQueryService.ResolveDefaults(request);
        var draftRule = new AlertRule
        {
            Id = Guid.Empty,
            Name = request.Name,
            Description = defaults.Description,
            Enabled = defaults.Enabled,
            Condition = request.Condition,
            Threshold = request.Threshold,
            WindowSeconds = request.WindowSeconds,
            CooldownSeconds = defaults.CooldownSeconds,
            WebhookUrl = defaults.WebhookUrl,
            TelegramBotToken = defaults.TelegramBotToken,
            TelegramChatId = defaults.TelegramChatId,
            EmailTo = defaults.EmailTo,
            PagerDutyRoutingKey = defaults.PagerDutyRoutingKey,
            CreatedAt = now,
            UpdatedAt = now,
            ConditionKind = defaults.ConditionKind,
            MetricCondition = request.MetricCondition,
            MetricThresholdValue = request.MetricThresholdValue,
        };

        var result = await SendTestAsync(notifier, draftRule, timeProvider, cancellationToken);
        return ApiSerialization.Write(http, result, AlertsJsonContext.Default.AlertNotificationTestResult);
    }

    /// <summary>
    /// Shared by both send-test endpoints: actually notifies through <paramref name="rule"/>'s
    /// configured channel with a synthetic (non-breaching) event and <c>isTest: true</c>
    /// wording, so a channel's config can be verified without waiting for a real breach -
    /// unlike <see cref="EvaluateAsync"/>, which never notifies.
    /// </summary>
    private static async Task<AlertNotificationTestResult> SendTestAsync(IAlertNotifier notifier, AlertRule rule, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var result = await notifier.SendAsync(rule, observedValue: 0, timeProvider.GetUtcNow(), cancellationToken, isTest: true);
        return new AlertNotificationTestResult
        {
            Success = result.Success,
            StatusCode = result.StatusCode,
            Error = result.Error ?? "",
        };
    }

    /// <summary>
    /// Shared by both test endpoints: evaluates the rule/draft's condition (log-filter
    /// count or metric-query threshold, per <paramref name="conditionKind"/>) over its
    /// window and reports whether the threshold would breach - without touching cooldown
    /// state or sending a notification (unlike <c>AlertEvaluationWorker</c>'s real
    /// evaluation). A <see cref="AlertConditionKind.MetricThreshold"/> draft/rule with no
    /// <paramref name="metricCondition"/>/<paramref name="metricThresholdValue"/> (an
    /// incomplete draft still being edited) reports "wouldn't fire" rather than throwing -
    /// <see cref="Model.AlertRuleRequest.ValidateCondition"/> is what rejects that shape on
    /// create/update; this dry-run endpoint is intentionally more lenient, same as it never
    /// calls <see cref="Model.AlertRuleRequest.ValidateChannel"/> either.
    /// </summary>
    private static async Task<AlertTestResult> EvaluateAsync(
        IAlertQueryService alerts,
        TimeProvider timeProvider,
        AlertConditionKind conditionKind,
        LogFilter condition,
        AlertThreshold threshold,
        MetricAlertCondition? metricCondition,
        double? metricThresholdValue,
        int windowSeconds,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var from = now - TimeSpan.FromSeconds(windowSeconds);

        if (conditionKind == AlertConditionKind.MetricThreshold)
        {
            if (metricCondition is null || metricThresholdValue is not { } thresholdValue)
            {
                return new AlertTestResult { ObservedCount = 0, WouldFire = false, EvaluatedAt = now, WindowSeconds = windowSeconds, ConditionKind = conditionKind, ObservedValue = null };
            }

            var value = await alerts.EvaluateMetricConditionAsync(metricCondition, from, now, cancellationToken);
            return new AlertTestResult
            {
                ObservedCount = 0,
                WouldFire = threshold.IsBreachedValue(value, thresholdValue),
                EvaluatedAt = now,
                WindowSeconds = windowSeconds,
                ConditionKind = conditionKind,
                ObservedValue = value,
            };
        }

        var count = await alerts.CountMatchingLogsAsync(condition, from, now, cancellationToken);
        return new AlertTestResult
        {
            ObservedCount = count,
            WouldFire = threshold.IsBreached(count),
            EvaluatedAt = now,
            WindowSeconds = windowSeconds,
            ConditionKind = conditionKind,
            ObservedValue = null,
        };
    }
}
