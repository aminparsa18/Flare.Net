using System.Text.Json;
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
        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        AlertRuleRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(http.Request.Body, AlertsJsonContext.Default.AlertRuleRequest, cancellationToken);
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

        var rule = await alerts.CreateAsync(request, cancellationToken);
        return Results.Json(rule, AlertsJsonContext.Default.AlertRule, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAsync(IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        var rules = await alerts.ListAsync(cancellationToken);
        return Results.Json(new AlertRuleListResponse { Rules = rules }, AlertsJsonContext.Default.AlertRuleListResponse);
    }

    private static async Task<IResult> HandleGetAsync(Guid id, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        var rule = await alerts.GetAsync(id, cancellationToken);
        return rule is null ? Results.NotFound() : Results.Json(rule, AlertsJsonContext.Default.AlertRule);
    }

    private static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        AlertRuleRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(http.Request.Body, AlertsJsonContext.Default.AlertRuleRequest, cancellationToken);
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

        var rule = await alerts.UpdateAsync(id, request, cancellationToken);
        return rule is null ? Results.NotFound() : Results.Json(rule, AlertsJsonContext.Default.AlertRule);
    }

    private static async Task<IResult> HandleDeleteAsync(Guid id, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        var deleted = await alerts.DeleteAsync(id, cancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> HandleHistoryAsync(Guid id, int? limit, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        var events = await alerts.GetHistoryAsync(id, limit is > 0 ? limit.Value : 50, cancellationToken);
        return Results.Json(new AlertHistoryResponse { Events = events }, AlertsJsonContext.Default.AlertHistoryResponse);
    }

    private static async Task<IResult> HandleTestSavedAsync(Guid id, IAlertQueryService alerts, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var rule = await alerts.GetAsync(id, cancellationToken);
        if (rule is null)
        {
            return Results.NotFound();
        }

        var result = await EvaluateAsync(alerts, timeProvider, rule.Condition, rule.Threshold, rule.WindowSeconds, cancellationToken);
        return Results.Json(result, AlertsJsonContext.Default.AlertTestResult);
    }

    private static async Task<IResult> HandleTestDraftAsync(HttpContext http, IAlertQueryService alerts, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        AlertRuleRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(http.Request.Body, AlertsJsonContext.Default.AlertRuleRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        var result = await EvaluateAsync(alerts, timeProvider, request.Condition, request.Threshold, request.WindowSeconds, cancellationToken);
        return Results.Json(result, AlertsJsonContext.Default.AlertTestResult);
    }

    /// <summary>
    /// Shared by both test endpoints: counts matching logs over the rule/draft's window
    /// and reports whether the threshold would breach - without touching cooldown state
    /// or sending a notification (unlike <c>AlertEvaluationWorker</c>'s real evaluation).
    /// </summary>
    private static async Task<AlertTestResult> EvaluateAsync(
        IAlertQueryService alerts,
        TimeProvider timeProvider,
        LogFilter condition,
        AlertThreshold threshold,
        int windowSeconds,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var from = now - TimeSpan.FromSeconds(windowSeconds);
        var count = await alerts.CountMatchingLogsAsync(condition, from, now, cancellationToken);
        return new AlertTestResult
        {
            ObservedCount = count,
            WouldFire = threshold.IsBreached(count),
            EvaluatedAt = now,
            WindowSeconds = windowSeconds,
        };
    }
}
