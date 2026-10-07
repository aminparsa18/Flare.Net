using System.Text.Json;
using Flare.Api.Alerting;
using Flare.Api.Auditing;
using Flare.Api.Auth;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Identity.Projects;
using Microsoft.Extensions.Options;

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
        endpoints.MapGet("/api/alerts/states", HandleStatesAsync);
        endpoints.MapGet("/api/alerts/{id:guid}", HandleGetAsync);
        endpoints.MapPut("/api/alerts/{id:guid}", HandleUpdateAsync);
        endpoints.MapDelete("/api/alerts/{id:guid}", HandleDeleteAsync);
        endpoints.MapGet("/api/alerts/{id:guid}/history", HandleHistoryAsync);
        // Acknowledge / snooze / clear the current incident of a firing rule (ADR-0124).
        endpoints.MapPost("/api/alerts/{id:guid}/ack", (Guid id, HttpContext http, IAlertQueryService alerts, TimeProvider time, CancellationToken ct) => HandleAckAsync(id, AlertAckKind.Ack, http, alerts, time, ct));
        endpoints.MapPost("/api/alerts/{id:guid}/snooze", (Guid id, HttpContext http, IAlertQueryService alerts, TimeProvider time, CancellationToken ct) => HandleAckAsync(id, AlertAckKind.Snooze, http, alerts, time, ct));
        endpoints.MapDelete("/api/alerts/{id:guid}/ack", (Guid id, HttpContext http, IAlertQueryService alerts, TimeProvider time, CancellationToken ct) => HandleAckAsync(id, AlertAckKind.Clear, http, alerts, time, ct));
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

        // Renders a draft's notification title/body (custom templates or the built-in
        // wording) with illustrative values - the rule form's live preview. Never sends and
        // never queries ClickHouse, so it's cheap enough to call on every (debounced) edit.
        endpoints.MapPost("/api/alerts/notification-preview", HandleNotificationPreviewAsync);

        // Portable JSON export/import of rules (channels and SLOs referenced by name).
        endpoints.MapGet("/api/alerts/export", HandleExportAsync);
        endpoints.MapPost("/api/alerts/import", HandleImportAsync);
        return endpoints;
    }

    // `ids` (optional, repeatable): export just those rules; omitted means every rule.
    private static async Task<IResult> HandleExportAsync(Guid[]? ids, HttpContext http, IAlertQueryService alerts, INotificationChannelQueryService channels, ISloQueryService slos, CancellationToken cancellationToken)
    {
        var access = http.GetProjectAccess();
        var rules = access.Filter(await alerts.ListAsync(cancellationToken), r => r.ProjectId);
        if (ids is { Length: > 0 })
        {
            var wanted = ids.ToHashSet();
            rules = [.. rules.Where(r => wanted.Contains(r.Id))];
        }

        var channelNames = (await channels.ListAsync(cancellationToken)).ToDictionary(c => c.Id, c => c.Name);
        var sloNames = access.Filter(await slos.ListAsync(cancellationToken), s => s.ProjectId).ToDictionary(s => s.Id, s => s.Name);
        return Results.Json(AlertRuleTransfer.Export(rules, channelNames, sloNames), AlertsJsonContext.Default.AlertRulesExport);
    }

    // `dryRun=true` reports what would happen without writing anything. A rule whose name
    // already exists is skipped, unless `update=true` (`flare config apply`), which replaces it
    // in place. A name repeated earlier in the same file is always skipped.
    private static async Task<IResult> HandleImportAsync(HttpContext http, bool? dryRun, bool? update, IAlertQueryService alerts, INotificationChannelQueryService channels, ISloQueryService slos, CancellationToken cancellationToken)
    {
        AlertRulesExport? document;
        try
        {
            document = await JsonSerializer.DeserializeAsync(http.Request.Body, AlertsJsonContext.Default.AlertRulesExport, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (document is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (document.Version != AlertRulesExport.CurrentVersion)
        {
            return Results.Problem($"Unsupported export version {document.Version} (expected {AlertRulesExport.CurrentVersion}).", statusCode: StatusCodes.Status400BadRequest);
        }

        var channelIds = AlertRuleTransfer.ByName((await channels.ListAsync(cancellationToken)).Select(c => (c.Id, c.Name)));
        var access = http.GetProjectAccess();
        var sloIds = AlertRuleTransfer.ByName(access.Filter(await slos.ListAsync(cancellationToken), s => s.ProjectId).Select(s => (s.Id, s.Name)));
        var existing = await alerts.ListAsync(cancellationToken);
        var taken = new HashSet<string>(existing.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);
        var updatable = update == true
            ? AlertRuleTransfer.ByName(access.Filter(existing, r => r.ProjectId).Select(r => (r.Id, r.Name)))
            : [];
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var results = new List<AlertImportItemResult>();
        foreach (var item in document.Rules)
        {
            var name = item.Rule?.Name ?? "";
            var (request, error) = AlertRuleTransfer.Resolve(item, channelIds, sloIds);
            if (error is not null)
            {
                results.Add(new AlertImportItemResult(name, AlertImportOutcome.Error, error));
                continue;
            }

            if (!seen.Add(request!.Name))
            {
                results.Add(new AlertImportItemResult(name, AlertImportOutcome.Skip, "A rule with this name appears earlier in the file."));
                continue;
            }

            if (taken.Contains(request.Name))
            {
                if (!updatable.TryGetValue(request.Name, out var existingId))
                {
                    results.Add(new AlertImportItemResult(name, AlertImportOutcome.Skip, "A rule with this name already exists."));
                    continue;
                }

                if (dryRun != true)
                {
                    await alerts.UpdateAsync(existingId, request, cancellationToken);
                }

                results.Add(new AlertImportItemResult(name, AlertImportOutcome.Update, Id: existingId));
                continue;
            }

            if (dryRun == true)
            {
                results.Add(new AlertImportItemResult(name, AlertImportOutcome.Create));
                continue;
            }

            var rule = await alerts.CreateAsync(request, cancellationToken);
            results.Add(new AlertImportItemResult(name, AlertImportOutcome.Create, Id: rule.Id));
        }

        return Results.Json(new AlertRulesImportResult { DryRun = dryRun == true, Items = results }, AlertsJsonContext.Default.AlertRulesImportResult);
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, IAlertQueryService alerts, IProjectStore projects, CancellationToken cancellationToken)
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

        if (await ProjectGuard.CheckTargetAsync(http, projects, null, ProjectGuard.Normalize(request.ProjectId), cancellationToken) is { } projectProblem)
        {
            return projectProblem;
        }

        if (NameUniqueness.Conflict((await alerts.ListAsync(cancellationToken)).Select(r => (r.Id, r.Name)), "alert rule", request.Name) is { } taken)
        {
            return taken;
        }

        var rule = await alerts.CreateAsync(request, cancellationToken);
        AuditContext.SetResourceId(http, rule.Id);
        return ApiSerialization.Write(http, NotificationSecrets.Redact(rule), AlertsJsonContext.Default.AlertRule, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAsync(HttpContext http, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        // Reads mask a rule's legacy inline credentials (see NotificationSecrets).
        var rules = http.GetProjectAccess().Filter(await alerts.ListAsync(cancellationToken), r => r.ProjectId);
        return ApiSerialization.Write(http, new AlertRuleListResponse { Rules = [.. rules.Select(NotificationSecrets.Redact)] }, AlertsJsonContext.Default.AlertRuleListResponse);
    }

    // JSON only: a small per-rule status list for the rules table, not worth a MemoryPack type.
    private static async Task<IResult> HandleStatesAsync(HttpContext http, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        var statuses = await alerts.GetRuleStatusesAsync(cancellationToken);
        var access = http.GetProjectAccess();
        if (!access.IsUnrestricted)
        {
            var visible = access.Filter(await alerts.ListAsync(cancellationToken), r => r.ProjectId).Select(r => r.Id).ToHashSet();
            statuses = [.. statuses.Where(s => visible.Contains(s.RuleId))];
        }

        return Results.Json(new AlertRuleStatusResponse(statuses), AlertsJsonContext.Default.AlertRuleStatusResponse);
    }

    private static async Task<IResult> HandleGetAsync(Guid id, HttpContext http, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        var rule = await alerts.GetAsync(id, cancellationToken);
        return rule is null || !http.GetProjectAccess().CanRead(rule.ProjectId) ? Results.NotFound() : ApiSerialization.Write(http, NotificationSecrets.Redact(rule), AlertsJsonContext.Default.AlertRule);
    }

    private static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, IAlertQueryService alerts, IProjectStore projects, CancellationToken cancellationToken)
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

        // A legacy inline secret sent back as its own mask means "unchanged".
        var before = await alerts.GetAsync(id, cancellationToken);
        if (before is not null)
        {
            if (ProjectGuard.CheckExisting(http, before.ProjectId) is { } denied)
            {
                return denied;
            }

            var target = ProjectGuard.ResolveForUpdate(before.ProjectId, request.ProjectId);
            if (await ProjectGuard.CheckTargetAsync(http, projects, before.ProjectId, target, cancellationToken) is { } projectProblem)
            {
                return projectProblem;
            }
        }

        request = NotificationSecrets.Restore(request, before);

        if (NameUniqueness.Conflict((await alerts.ListAsync(cancellationToken)).Select(r => (r.Id, r.Name)), "alert rule", request.Name, id, before?.Name) is { } taken)
        {
            return taken;
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
        if (rule is not null)
        {
            AuditContext.SetChange(http, AlertsJsonContext.Default.AlertRule, NotificationSecrets.RedactOrNull(before), NotificationSecrets.Redact(rule));
        }

        return rule is null ? Results.NotFound() : ApiSerialization.Write(http, NotificationSecrets.Redact(rule), AlertsJsonContext.Default.AlertRule);
    }

    private static async Task<IResult> HandleDeleteAsync(Guid id, HttpContext http, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        if (await alerts.GetAsync(id, cancellationToken) is { } existing && ProjectGuard.CheckExisting(http, existing.ProjectId) is { } denied)
        {
            return denied;
        }

        var deleted = await alerts.DeleteAsync(id, cancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> HandleAckAsync(Guid id, AlertAckKind kind, HttpContext http, IAlertQueryService alerts, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var rule = await alerts.GetAsync(id, cancellationToken);
        if (rule is null)
        {
            return Results.NotFound();
        }

        if (ProjectGuard.CheckExisting(http, rule.ProjectId) is { } denied)
        {
            return denied;
        }

        AlertAckRequest? request = null;
        if (kind != AlertAckKind.Clear && http.Request.ContentLength is > 0)
        {
            try
            {
                request = await ApiSerialization.ReadAsync(http, AlertsJsonContext.Default.AlertAckRequest, cancellationToken);
            }
            catch (JsonException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }
        }

        DateTimeOffset? until = null;
        var now = timeProvider.GetUtcNow();
        if (kind == AlertAckKind.Snooze)
        {
            if (AlertAckPolicy.ValidateSnooze(request?.SnoozeMinutes) is { } error)
            {
                return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
            }

            until = now.AddMinutes(request!.SnoozeMinutes!.Value);
        }

        // Only a firing rule has an incident to acknowledge; a clear is allowed whenever one is.
        var states = await alerts.GetFiringStatesAsync([id], cancellationToken);
        if (!states.TryGetValue(id, out var state))
        {
            return Results.Problem("The alert rule isn't firing, so there is nothing to acknowledge.", statusCode: StatusCodes.Status409Conflict);
        }

        var note = (request?.Note ?? "").Trim();
        if (note.Length > 500)
        {
            return Results.Problem("note can be at most 500 characters.", statusCode: StatusCodes.Status400BadRequest);
        }

        var ack = new AlertAck(id, now, http.User.Identity?.Name ?? "", kind, until, note);
        await alerts.InsertAckAsync(ack, cancellationToken);
        var effective = AlertAckPolicy.Effective(ack, state.LastResolvedAt);
        return Results.Json(new AlertRuleStatus(id, true, state.LastFiredAt, effective), AlertsJsonContext.Default.AlertRuleStatus);
    }

    private static async Task<IResult> HandleHistoryAsync(Guid id, int? limit, HttpContext http, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        if (await alerts.GetAsync(id, cancellationToken) is { } owner && !http.GetProjectAccess().CanRead(owner.ProjectId))
        {
            return Results.NotFound();
        }

        var events = await alerts.GetHistoryAsync(id, limit is > 0 ? limit.Value : 50, cancellationToken);
        return ApiSerialization.Write(http, new AlertHistoryResponse { Events = events }, AlertsJsonContext.Default.AlertHistoryResponse);
    }

    private static async Task<IResult> HandleTestSavedAsync(Guid id, HttpContext http, IAlertQueryService alerts, ISloQueryService slos, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var rule = await alerts.GetAsync(id, cancellationToken);
        if (rule is null || !http.GetProjectAccess().CanRead(rule.ProjectId))
        {
            return Results.NotFound();
        }

        var result = await EvaluateAsync(alerts, slos, timeProvider, rule.ConditionKind, rule.Condition, rule.Threshold, rule.MetricCondition, rule.MetricThresholdValue, rule.ThresholdUnit, rule.ExceptionCondition, rule.AnomalyCondition, rule.SloCondition, rule.WindowSeconds, rule.NoDataWindowSeconds, rule.MinDataPoints, cancellationToken);
        return ApiSerialization.Write(http, result, AlertsJsonContext.Default.AlertTestResult);
    }

    private static async Task<IResult> HandleTestDraftAsync(HttpContext http, IAlertQueryService alerts, ISloQueryService slos, TimeProvider timeProvider, CancellationToken cancellationToken)
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

        var result = await EvaluateAsync(alerts, slos, timeProvider, request.ConditionKind ?? AlertConditionKind.LogCount, request.Condition, request.Threshold, request.MetricCondition, request.MetricThresholdValue, request.ThresholdUnit, request.ExceptionCondition, request.AnomalyCondition, request.SloCondition, request.WindowSeconds, request.NoDataWindowSeconds ?? 0, request.MinDataPoints ?? 0, cancellationToken);
        return ApiSerialization.Write(http, result, AlertsJsonContext.Default.AlertTestResult);
    }

    private static async Task<IResult> HandleSendTestSavedAsync(Guid id, HttpContext http, IAlertQueryService alerts, INotificationChannelQueryService channels, CompositeAlertNotifier notifier, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var rule = await alerts.GetAsync(id, cancellationToken);
        if (rule is null || !http.GetProjectAccess().CanRead(rule.ProjectId))
        {
            return Results.NotFound();
        }

        var result = await SendTestAsync(notifier, channels, rule, timeProvider, cancellationToken);
        return ApiSerialization.Write(http, result, AlertsJsonContext.Default.AlertNotificationTestResult);
    }

    // `ruleId` (optional, query string): the saved rule an edit form is testing, so a legacy
    // inline secret the form still holds only as its mask resolves to the stored value.
    private static async Task<IResult> HandleSendTestDraftAsync(HttpContext http, Guid? ruleId, IAlertQueryService alerts, INotificationChannelQueryService channels, CompositeAlertNotifier notifier, TimeProvider timeProvider, CancellationToken cancellationToken)
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

        if (ruleId is { } savedId)
        {
            var saved = await alerts.GetAsync(savedId, cancellationToken);
            request = NotificationSecrets.Restore(request, saved is not null && http.GetProjectAccess().CanRead(saved.ProjectId) ? saved : null);
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

        var draftRule = ToDraftRule(request, timeProvider.GetUtcNow());
        var result = await SendTestAsync(notifier, channels, draftRule, timeProvider, cancellationToken);
        return ApiSerialization.Write(http, result, AlertsJsonContext.Default.AlertNotificationTestResult);
    }

    /// <summary>
    /// An unsaved <see cref="AlertRule"/> built from a request body (<see cref="Guid.Empty"/>
    /// id) - what the draft send-test and notification-preview endpoints hand to the notifier/
    /// formatter, so both see exactly the rule create/update would persist.
    /// </summary>
    private static AlertRule ToDraftRule(AlertRuleRequest request, DateTimeOffset now)
    {
        var defaults = AlertQueryService.ResolveDefaults(request);
        return new AlertRule
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
            ChannelIds = defaults.ChannelIds,
            ExceptionCondition = request.ExceptionCondition,
            NoDataWindowSeconds = defaults.NoDataWindowSeconds,
            EvaluationIntervalSeconds = defaults.EvaluationIntervalSeconds,
            AnomalyCondition = request.AnomalyCondition,
            MinDataPoints = defaults.MinDataPoints,
            NotificationTitleTemplate = defaults.NotificationTitleTemplate,
            NotificationBodyTemplate = defaults.NotificationBodyTemplate,
            RecoveryThreshold = request.RecoveryThreshold,
            Severity = defaults.Severity,
            ThresholdUnit = defaults.ThresholdUnit,
            Labels = defaults.Labels,
            SloCondition = request.SloCondition,
        };
    }

    private static async Task<IResult> HandleNotificationPreviewAsync(Guid? ruleId, HttpContext http, IOptions<AlertLinkOptions> linkOptions, TimeProvider timeProvider, CancellationToken cancellationToken)
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

        // Only the templates are validated - the rest of the draft may still be half-filled
        // while the user types, and an invalid template is reported in-band (Error) so the
        // form can show it next to the preview rather than as a failed request.
        // ?ruleId= (the form sends it when editing a saved rule) only makes {{rule_id}}/
        // {{rule_url}} show the real rule rather than the empty draft GUID.
        var rule = ToDraftRule(request, timeProvider.GetUtcNow()) with { Id = ruleId ?? Guid.Empty };
        var (observedValue, anomaly) = PreviewSample(rule);
        var message = AlertMessageFormatter.BuildMessage(rule, observedValue, isTest: false, linkOptions.Value.PublicUrl, metricUnit: string.IsNullOrEmpty(rule.ThresholdUnit) ? null : rule.ThresholdUnit, rule.UpdatedAt, noData: false, anomaly);
        string ForFormat(AlertMarkupFormat format) => AlertMessageFormatter.BuildMessage(rule, observedValue, isTest: false, linkOptions.Value.PublicUrl, metricUnit: string.IsNullOrEmpty(rule.ThresholdUnit) ? null : rule.ThresholdUnit, rule.UpdatedAt, noData: false, anomaly, format: format).Combined;
        var hasTemplate = message.IsCustom;
        var preview = new AlertNotificationPreview
        {
            Title = message.Title ?? "",
            Text = message.Text,
            Error = request.ValidateTemplates() ?? "",
            TelegramHtml = hasTemplate ? ForFormat(AlertMarkupFormat.TelegramHtml) : "",
            SlackText = hasTemplate ? ForFormat(AlertMarkupFormat.SlackMrkdwn) : "",
            EmailHtml = string.IsNullOrEmpty(rule.NotificationBodyTemplate)
                ? ""
                : AlertMessageFormatter.BuildMessage(rule, observedValue, isTest: false, linkOptions.Value.PublicUrl, metricUnit: string.IsNullOrEmpty(rule.ThresholdUnit) ? null : rule.ThresholdUnit, rule.UpdatedAt, noData: false, anomaly, format: AlertMarkupFormat.EmailHtml).Text,
        };
        return ApiSerialization.Write(http, preview, AlertsJsonContext.Default.AlertNotificationPreview);
    }

    /// <summary>
    /// Illustrative values for the notification preview: a value sitting right on the
    /// threshold (so it reads as a breach), and for an anomaly rule a made-up baseline three
    /// standard deviations away. The preview never queries ClickHouse - the dry-run "Test"
    /// action is what shows a rule's real current value.
    /// </summary>
    internal static (double ObservedValue, AnomalyScore? Anomaly) PreviewSample(AlertRule rule)
    {
        if (rule.ConditionKind == AlertConditionKind.Anomaly)
        {
            return (150, new AnomalyScore(Current: 150, SampleCount: rule.AnomalyCondition?.BaselinePeriods ?? 0, BaselineMean: 100, ZScore: 3, Breached: true));
        }

        if (rule.ConditionKind == AlertConditionKind.SloBurnRate)
        {
            return (rule.SloCondition?.BurnRateThreshold ?? 0, null);
        }

        var breaches = rule.Threshold.Comparator == ThresholdComparator.GreaterThanOrEqual;
        return rule.ConditionKind == AlertConditionKind.MetricThreshold
            ? (rule.MetricThresholdValue ?? 0, null)
            : (breaches ? rule.Threshold.Count : 0, null);
    }

    /// <summary>
    /// Shared by both send-test endpoints: resolves <paramref name="rule"/>'s configured
    /// channel(s) (via <see cref="NotificationChannelResolver"/>) and actually notifies
    /// every one of them with a synthetic (non-breaching) event and <c>isTest: true</c>
    /// wording, so a channel's config can be verified without waiting for a real breach -
    /// unlike <see cref="EvaluateAsync"/>, which never notifies. Aggregates a fan-out
    /// rule's per-channel results into one <see cref="AlertNotificationTestResult"/>
    /// (success only if every channel succeeded, errors joined) rather than expanding
    /// that response type into a per-channel list - the same "single summary, per-channel
    /// detail lives in history instead" tradeoff <see cref="AlertHistoryEntry"/>'s own
    /// <see cref="AlertHistoryEntry.NotificationStatus"/> vs
    /// <see cref="AlertHistoryEntry.ChannelResults"/> makes.
    /// </summary>
    private static async Task<AlertNotificationTestResult> SendTestAsync(CompositeAlertNotifier notifier, INotificationChannelQueryService channelStore, AlertRule rule, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        var channels = await NotificationChannelResolver.ResolveAsync(rule, channelStore, cancellationToken);
        if (channels.Count == 0)
        {
            return new AlertNotificationTestResult { Success = false, StatusCode = 0, Error = "This rule/draft has no notification channel configured." };
        }

        var results = await notifier.SendAllAsync(rule, channels, observedValue: 0, timeProvider.GetUtcNow(), cancellationToken, isTest: true);
        var failures = results.Where(r => !r.Success).ToList();
        return new AlertNotificationTestResult
        {
            Success = failures.Count == 0,
            StatusCode = results[0].StatusCode,
            Error = failures.Count == 0 ? "" : string.Join("; ", failures.Select(r => r.Error ?? "unknown error")),
        };
    }

    /// <summary>
    /// Shared by both test endpoints: evaluates the rule/draft's condition (log-filter
    /// count, metric-query threshold, or exception-occurrence count, per
    /// <paramref name="conditionKind"/>) over its window and reports whether the threshold
    /// would breach - without touching cooldown state or sending a notification (unlike
    /// <c>AlertEvaluationWorker</c>'s real evaluation). A <see cref="AlertConditionKind.MetricThreshold"/>
    /// draft/rule with no <paramref name="metricCondition"/>/<paramref name="metricThresholdValue"/>,
    /// or a <see cref="AlertConditionKind.ExceptionCount"/> draft/rule with no
    /// <paramref name="exceptionCondition"/> (an incomplete draft still being edited) reports
    /// "wouldn't fire" rather than throwing - <see cref="Model.AlertRuleRequest.ValidateCondition"/>
    /// is what rejects that shape on create/update; this dry-run endpoint is intentionally
    /// more lenient, same as it never calls <see cref="Model.AlertRuleRequest.ValidateChannel"/>
    /// either. With <paramref name="noDataWindowSeconds"/> enabled, an absent-data result
    /// (<see cref="AlertNoDataEvaluator"/>) takes precedence over the threshold, same as
    /// <c>AlertEvaluationWorker</c>. An <see cref="AlertConditionKind.Anomaly"/> rule/draft is
    /// scored through <see cref="AnomalyEvaluator"/>, the same path the worker uses. A metric
    /// rule/draft with <paramref name="minDataPoints"/> enabled reports
    /// <see cref="AlertTestResult.InsufficientData"/> (and never fires) below that many points
    /// (<see cref="AlertMinDataPointsEvaluator"/>).
    /// </summary>
    private static async Task<AlertTestResult> EvaluateAsync(
        IAlertQueryService alerts,
        ISloQueryService slos,
        TimeProvider timeProvider,
        AlertConditionKind conditionKind,
        LogFilter condition,
        AlertThreshold threshold,
        MetricAlertCondition? metricCondition,
        double? metricThresholdValue,
        string? thresholdUnit,
        ExceptionCountCondition? exceptionCondition,
        AnomalyCondition? anomalyCondition,
        SloBurnRateCondition? sloCondition,
        int windowSeconds,
        int noDataWindowSeconds,
        int minDataPoints,
        CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var from = now - TimeSpan.FromSeconds(windowSeconds);

        var seriesKind = AnomalyScoring.SeriesKind(conditionKind, anomalyCondition);
        if (await AlertNoDataEvaluator.IsAbsentAsync(alerts, seriesKind, condition, metricCondition, noDataWindowSeconds, now, cancellationToken))
        {
            return new AlertTestResult { ObservedCount = 0, WouldFire = true, EvaluatedAt = now, WindowSeconds = noDataWindowSeconds, ConditionKind = conditionKind, ObservedValue = null, NoData = true };
        }

        if (conditionKind == AlertConditionKind.MetricThreshold)
        {
            if (metricCondition is null || metricThresholdValue is not { } thresholdValue)
            {
                return new AlertTestResult { ObservedCount = 0, WouldFire = false, EvaluatedAt = now, WindowSeconds = windowSeconds, ConditionKind = conditionKind, ObservedValue = null };
            }

            var pointCount = await AlertMinDataPointsEvaluator.CountAsync(alerts, metricCondition, minDataPoints, from, now, cancellationToken);
            var insufficient = AlertMinDataPointsEvaluator.IsInsufficient(minDataPoints, pointCount);
            var (value, seriesUnit) = await alerts.EvaluateMetricConditionAsync(metricCondition, from, now, cancellationToken);
            return new AlertTestResult
            {
                ObservedCount = 0,
                WouldFire = !insufficient && threshold.IsBreachedValue(value, MetricUnitConverter.ToSeriesUnit(thresholdValue, thresholdUnit, seriesUnit)),
                EvaluatedAt = now,
                WindowSeconds = windowSeconds,
                ConditionKind = conditionKind,
                // NaN (empty window) isn't writable as JSON - same null mapping as the Anomaly branch below.
                ObservedValue = double.IsNaN(value) ? null : value,
                InsufficientData = insufficient,
                DataPointCount = pointCount,
            };
        }

        if (conditionKind == AlertConditionKind.Anomaly)
        {
            var evaluated = anomalyCondition is null
                ? null
                : await AnomalyEvaluator.EvaluateAsync(alerts, anomalyCondition, condition, metricCondition, exceptionCondition, windowSeconds, now, cancellationToken);
            var score = evaluated?.Score;
            return new AlertTestResult
            {
                ObservedCount = 0,
                WouldFire = score?.Breached ?? false,
                EvaluatedAt = now,
                WindowSeconds = windowSeconds,
                ConditionKind = conditionKind,
                // NaN (a metric source with no points) has no JSON representation.
                ObservedValue = score is null || double.IsNaN(score.Current) ? null : score.Current,
                BaselineMean = score?.BaselineMean,
                ZScore = score?.ZScore,
                BaselineSampleCount = score?.SampleCount ?? 0,
            };
        }

        if (conditionKind == AlertConditionKind.SloBurnRate)
        {
            // A draft with no (or a deleted) SLO reports "wouldn't fire", same leniency as the other kinds.
            var burn = sloCondition is null ? null : await SloBurnRateEvaluator.EvaluateAsync(slos, sloCondition, cancellationToken);
            return new AlertTestResult
            {
                ObservedCount = 0,
                WouldFire = burn?.Breached ?? false,
                EvaluatedAt = now,
                WindowSeconds = windowSeconds,
                ConditionKind = conditionKind,
                ObservedValue = burn?.LongBurnRate,
            };
        }

        if (conditionKind == AlertConditionKind.ExceptionCount)
        {
            if (exceptionCondition is null)
            {
                return new AlertTestResult { ObservedCount = 0, WouldFire = false, EvaluatedAt = now, WindowSeconds = windowSeconds, ConditionKind = conditionKind, ObservedValue = null };
            }

            var exceptionCount = await alerts.CountMatchingExceptionsAsync(exceptionCondition, from, now, cancellationToken);
            return new AlertTestResult
            {
                ObservedCount = exceptionCount,
                WouldFire = threshold.IsBreached(exceptionCount),
                EvaluatedAt = now,
                WindowSeconds = windowSeconds,
                ConditionKind = conditionKind,
                ObservedValue = null,
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
