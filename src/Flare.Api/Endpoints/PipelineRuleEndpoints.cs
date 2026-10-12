using System.Text.Json;
using Flare.Api.Auditing;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>The pipeline-rule API: field-extraction/redaction rule CRUD, plus preview dry-runs, under <c>/api/pipeline-rules</c>.</summary>
/// <remarks>
/// Mirrors <see cref="AlertEndpoints"/>'s shape: POST/PUT + JSON body, manual
/// <see cref="JsonSerializer.DeserializeAsync"/> against the source-gen
/// <see cref="PipelineRulesJsonContext"/>, <see cref="Results.Problem"/> on 400s. The
/// preview saved/draft pair also mirrors <see cref="AlertEndpoints"/>'s <c>/test</c> pair -
/// see docs-internal/adr/0034-pipeline-rules-preview.md.
/// </remarks>
public static class PipelineRuleEndpoints
{
    /// <summary>
    /// Rows pulled for a preview sample - small on purpose: this is rendered in a dialog
    /// for a human to read before/after pairs, not a data table, so it's far below
    /// <see cref="Query.LogSearchQueryBuilder.DefaultPageSize"/>.
    /// </summary>
    public const int PreviewSampleSize = 20;

    /// <summary>Rows scanned by the managed-PII preview (see <see cref="HandlePiiPreviewAsync"/>).</summary>
    public const int PiiPreviewSampleSize = 500;

    public static IEndpointRouteBuilder MapPipelineRuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/pipeline-rules", HandleCreateAsync);
        endpoints.MapGet("/api/pipeline-rules", HandleListAsync);
        endpoints.MapGet("/api/pipeline-rules/{id:guid}", HandleGetAsync);
        endpoints.MapPut("/api/pipeline-rules/{id:guid}", HandleUpdateAsync);
        endpoints.MapDelete("/api/pipeline-rules/{id:guid}", HandleDeleteAsync);
        // Saved-rule preview first (more specific route) so it doesn't get shadowed by the
        // draft-rule route below - same ordering reason AlertEndpoints' /test pair documents.
        endpoints.MapPost("/api/pipeline-rules/{id:guid}/preview", HandlePreviewSavedAsync);
        endpoints.MapPost("/api/pipeline-rules/preview", HandlePreviewDraftAsync);
        endpoints.MapPost("/api/pipeline-rules/pii-preview", HandlePiiPreviewAsync);
        endpoints.MapPost("/api/pipeline-rules/pii-scan", HandlePiiScanAsync);
        endpoints.MapPost("/api/pipeline-rules/pii-span-preview", HandlePiiSpanPreviewAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, IPipelineRuleQueryService rules, CancellationToken cancellationToken)
    {
        var (request, error) = await ReadRequestAsync(http, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (request!.Validate() is { } validationError)
        {
            return Results.Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
        }

        if (NameUniqueness.Conflict((await rules.ListAsync(cancellationToken)).Select(r => (r.Id, r.Name)), "pipeline rule", request.Name) is { } taken)
        {
            return taken;
        }

        var rule = await rules.CreateAsync(request, cancellationToken);
        AuditContext.SetResourceId(http, rule.Id);
        return ApiSerialization.Write(http, rule, PipelineRulesJsonContext.Default.PipelineRule, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAsync(HttpContext http, IPipelineRuleQueryService rules, CancellationToken cancellationToken)
    {
        var list = await rules.ListAsync(cancellationToken);
        return ApiSerialization.Write(http, new PipelineRuleListResponse { Rules = list }, PipelineRulesJsonContext.Default.PipelineRuleListResponse);
    }

    private static async Task<IResult> HandleGetAsync(Guid id, HttpContext http, IPipelineRuleQueryService rules, CancellationToken cancellationToken)
    {
        var rule = await rules.GetAsync(id, cancellationToken);
        return rule is null ? Results.NotFound() : ApiSerialization.Write(http, rule, PipelineRulesJsonContext.Default.PipelineRule);
    }

    private static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, IPipelineRuleQueryService rules, CancellationToken cancellationToken)
    {
        var (request, error) = await ReadRequestAsync(http, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (request!.Validate() is { } validationError)
        {
            return Results.Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
        }

        var before = await rules.GetAsync(id, cancellationToken);
        if (NameUniqueness.Conflict((await rules.ListAsync(cancellationToken)).Select(r => (r.Id, r.Name)), "pipeline rule", request.Name, id, before?.Name) is { } taken)
        {
            return taken;
        }

        var rule = await rules.UpdateAsync(id, request, cancellationToken);
        if (rule is not null)
        {
            AuditContext.SetChange(http, PipelineRulesJsonContext.Default.PipelineRule, before, rule);
        }

        return rule is null ? Results.NotFound() : ApiSerialization.Write(http, rule, PipelineRulesJsonContext.Default.PipelineRule);
    }

    private static async Task<IResult> HandleDeleteAsync(Guid id, IPipelineRuleQueryService rules, CancellationToken cancellationToken)
    {
        var deleted = await rules.DeleteAsync(id, cancellationToken);
        return deleted ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> HandlePreviewSavedAsync(Guid id, HttpContext http, IPipelineRuleQueryService rules, ILogQueryService logs, CancellationToken cancellationToken)
    {
        var rule = await rules.GetAsync(id, cancellationToken);
        if (rule is null)
        {
            return Results.NotFound();
        }

        var result = await PreviewAsync(logs, rule.Condition, rule.Actions, cancellationToken);
        return ApiSerialization.Write(http, result, PipelineRulesJsonContext.Default.PipelineRulePreviewResult);
    }

    private static async Task<IResult> HandlePreviewDraftAsync(HttpContext http, ILogQueryService logs, CancellationToken cancellationToken)
    {
        var (request, error) = await ReadRequestAsync(http, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var result = await PreviewAsync(logs, request!.Condition, request.Actions ?? [], cancellationToken);
        return ApiSerialization.Write(http, result, PipelineRulesJsonContext.Default.PipelineRulePreviewResult);
    }

    /// <summary>
    /// Report-only scan (ADR-0199): over the <see cref="PiiPreviewSampleSize"/> most recent logs, counts per
    /// field (body or attribute key) and detector how many logs contain a match, with one masked example.
    /// Tells an operator where PII lives before enabling redaction. Writes nothing.
    /// </summary>
    private static async Task<IResult> HandlePiiScanAsync(HttpContext http, ILogQueryService logs, ISpanQueryService spans, CancellationToken cancellationToken)
    {
        PiiPreviewRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, PipelineRulesJsonContext.Default.PiiPreviewRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        request ??= new PiiPreviewRequest();
        var selected = PiiDetectors.All;
        if (!string.IsNullOrWhiteSpace(request.Detectors) && !Enum.TryParse(request.Detectors, ignoreCase: true, out selected))
        {
            return Results.Problem($"Unknown detectors '{request.Detectors}'. Valid: {string.Join(", ", Enum.GetNames<PiiDetectors>())}.", statusCode: StatusCodes.Status400BadRequest);
        }

        var found = new Dictionary<(string Field, PiiDetectors Detector), (int Count, string Example)>();
        int sampled;
        if (string.Equals(request.Signal, "spans", StringComparison.OrdinalIgnoreCase))
        {
            var sample = await spans.SearchAsync(new SpanSearchRequest { Filter = request.SpanCondition, PageSize = PiiPreviewSampleSize }, cancellationToken);
            sampled = sample.Spans.Count;
            foreach (var span in sample.Spans)
            {
                foreach (var (field, value) in PiiSpanFields.Enumerate(span))
                {
                    Scan(field, value, selected, found);
                }
            }
        }
        else if (string.IsNullOrWhiteSpace(request.Signal) || string.Equals(request.Signal, "logs", StringComparison.OrdinalIgnoreCase))
        {
            var sample = await logs.SearchAsync(new LogSearchRequest { Filter = request.Condition, PageSize = PiiPreviewSampleSize }, cancellationToken);
            sampled = sample.Events.Count;
            foreach (var logEvent in sample.Events)
            {
                Scan("body", logEvent.Body, selected, found);
                foreach (var (key, value) in logEvent.LogAttributes)
                {
                    Scan(key, value, selected, found);
                }
            }
        }
        else
        {
            return Results.Problem($"Unknown signal '{request.Signal}'. Valid: logs, spans.", statusCode: StatusCodes.Status400BadRequest);
        }

        var findings = found
            .OrderByDescending(f => f.Value.Count)
            .ThenBy(f => f.Key.Field, StringComparer.Ordinal)
            .Select(f => new PiiScanFinding { Field = f.Key.Field, Detector = f.Key.Detector.ToString(), Count = f.Value.Count, Example = f.Value.Example })
            .ToList();
        return ApiSerialization.Write(http, new PiiScanResult { SampledCount = sampled, Findings = findings }, PipelineRulesJsonContext.Default.PiiScanResult);
    }

    /// <summary>Examples are masked with every detector, not just the matching one, so a body holding an email and a card never leaks the other.</summary>
    private static string Truncate(string text) => text.Length > 200 ? text[..200] : text;

    private static void Scan(string field, string value, PiiDetectors selected, Dictionary<(string, PiiDetectors), (int Count, string Example)> found)
    {
        foreach (var detector in Enum.GetValues<PiiDetectors>())
        {
            if (detector is PiiDetectors.None or PiiDetectors.All || !selected.HasFlag(detector))
            {
                continue;
            }

            var masked = PiiRedactor.Redact(value, detector);
            if (masked == value)
            {
                continue;
            }

            found[(field, detector)] = found.TryGetValue((field, detector), out var existing)
                ? (existing.Count + 1, existing.Example)
                : (1, Truncate(PiiRedactor.Redact(value, PiiDetectors.All)));
        }
    }

    /// <summary>
    /// Span counterpart of <see cref="HandlePiiPreviewAsync"/>: over the <see cref="PiiPreviewSampleSize"/> most recent
    /// spans matching <c>SpanCondition</c>, returns the (at most <see cref="PreviewSampleSize"/>) spans whose status
    /// message or attributes would change, as field-level before/after pairs.
    /// </summary>
    private static async Task<IResult> HandlePiiSpanPreviewAsync(HttpContext http, ISpanQueryService spans, CancellationToken cancellationToken)
    {
        PiiPreviewRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, PipelineRulesJsonContext.Default.PiiPreviewRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        request ??= new PiiPreviewRequest();
        var detectors = PiiDetectors.All;
        if (!string.IsNullOrWhiteSpace(request.Detectors) && !Enum.TryParse(request.Detectors, ignoreCase: true, out detectors))
        {
            return Results.Problem($"Unknown detectors '{request.Detectors}'. Valid: {string.Join(", ", Enum.GetNames<PiiDetectors>())}.", statusCode: StatusCodes.Status400BadRequest);
        }

        var sample = await spans.SearchAsync(new SpanSearchRequest { Filter = request.SpanCondition, PageSize = PiiPreviewSampleSize }, cancellationToken);
        var matches = new List<PiiSpanPreviewMatch>();
        var changedCount = 0;
        foreach (var span in sample.Spans)
        {
            var changes = new List<PiiFieldChange>();
            foreach (var (field, value) in PiiSpanFields.Enumerate(span))
            {
                var redacted = PiiRedactor.Redact(value, detectors);
                if (redacted != value)
                {
                    changes.Add(new PiiFieldChange { Field = field, Before = value, After = redacted });
                }
            }

            if (changes.Count == 0)
            {
                continue;
            }

            changedCount++;
            if (matches.Count < PreviewSampleSize)
            {
                matches.Add(new PiiSpanPreviewMatch { TraceId = span.TraceId, SpanId = span.SpanId, StartTime = span.StartTime, ServiceName = span.ServiceName, Name = span.Name, Changes = changes });
            }
        }

        return ApiSerialization.Write(http, new PiiSpanPreviewResult { SampledCount = sample.Spans.Count, ChangedCount = changedCount, Matches = matches }, PipelineRulesJsonContext.Default.PiiSpanPreviewResult);
    }

    /// <summary>
    /// Dry-run of Ingest's managed PII redaction (ADR-0199) over a wider recent sample
    /// (<see cref="PiiPreviewSampleSize"/>), returning only logs it would change (capped at
    /// <see cref="PreviewSampleSize"/>) - most logs hold no PII, so a 20-row sample would
    /// usually show nothing. <c>SampledCount</c> is how many were scanned.
    /// </summary>
    private static async Task<IResult> HandlePiiPreviewAsync(HttpContext http, ILogQueryService logs, CancellationToken cancellationToken)
    {
        PiiPreviewRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, PipelineRulesJsonContext.Default.PiiPreviewRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        request ??= new PiiPreviewRequest();
        var detectors = PiiDetectors.All;
        if (!string.IsNullOrWhiteSpace(request.Detectors) && !Enum.TryParse(request.Detectors, ignoreCase: true, out detectors))
        {
            return Results.Problem($"Unknown detectors '{request.Detectors}'. Valid: {string.Join(", ", Enum.GetNames<PiiDetectors>())}.", statusCode: StatusCodes.Status400BadRequest);
        }

        var sample = await logs.SearchAsync(new LogSearchRequest { Filter = request.Condition, PageSize = PiiPreviewSampleSize }, cancellationToken);
        var matches = new List<PipelineRulePreviewMatch>();
        var changedCount = 0;
        foreach (var logEvent in sample.Events)
        {
            var body = PiiRedactor.Redact(logEvent.Body, detectors);
            Dictionary<string, string>? attributes = null;
            foreach (var (key, value) in logEvent.LogAttributes)
            {
                var redacted = PiiRedactor.Redact(value, detectors);
                if (redacted != value)
                {
                    attributes ??= new Dictionary<string, string>(logEvent.LogAttributes);
                    attributes[key] = redacted;
                }
            }

            if (body == logEvent.Body && attributes is null)
            {
                continue;
            }

            changedCount++;
            if (matches.Count < PreviewSampleSize)
            {
                matches.Add(new PipelineRulePreviewMatch
                {
                    EventId = logEvent.EventId,
                    Timestamp = logEvent.Timestamp,
                    ServiceName = logEvent.ServiceName,
                    BeforeBody = logEvent.Body,
                    AfterBody = body,
                    BeforeAttributes = logEvent.LogAttributes,
                    AfterAttributes = attributes ?? logEvent.LogAttributes,
                    Changed = true,
                });
            }
        }

        var result = new PipelineRulePreviewResult { SampledCount = sample.Events.Count, ChangedCount = changedCount, Matches = matches };
        return ApiSerialization.Write(http, result, PipelineRulesJsonContext.Default.PipelineRulePreviewResult);
    }

    /// <summary>
    /// Shared by both preview endpoints: samples up to <see cref="PreviewSampleSize"/>
    /// most-recent logs already matching <paramref name="condition"/> (the search itself
    /// does the condition matching - see <see cref="Query.PipelineRuleActionExecutor"/>'s
    /// remarks for why there's no separate in-memory re-check here), then applies
    /// <paramref name="actions"/> to each in-process to produce a before/after pair -
    /// without writing anything back. A rule/draft with no actions yet (still being
    /// authored) still returns the sample with every <c>Changed</c> flag false, same
    /// leniency <c>AlertEndpoints.EvaluateAsync</c> extends to an incomplete draft.
    /// </summary>
    private static async Task<PipelineRulePreviewResult> PreviewAsync(ILogQueryService logs, LogFilter condition, IReadOnlyList<PipelineRuleAction> actions, CancellationToken cancellationToken)
    {
        var sample = await logs.SearchAsync(new LogSearchRequest { Filter = condition, PageSize = PreviewSampleSize }, cancellationToken);

        var matches = new List<PipelineRulePreviewMatch>(sample.Events.Count);
        var changedCount = 0;
        foreach (var logEvent in sample.Events)
        {
            var after = PipelineRuleActionExecutor.Apply(logEvent, actions);
            var changed = after.Body != logEvent.Body || !AttributesEqual(logEvent.LogAttributes, after.LogAttributes);
            if (changed)
            {
                changedCount++;
            }

            matches.Add(new PipelineRulePreviewMatch
            {
                EventId = logEvent.EventId,
                Timestamp = logEvent.Timestamp,
                ServiceName = logEvent.ServiceName,
                BeforeBody = logEvent.Body,
                AfterBody = after.Body,
                BeforeAttributes = logEvent.LogAttributes,
                AfterAttributes = after.LogAttributes,
                Changed = changed,
            });
        }

        return new PipelineRulePreviewResult { SampledCount = matches.Count, ChangedCount = changedCount, Matches = matches };
    }

    private static bool AttributesEqual(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after)
    {
        if (before.Count != after.Count)
        {
            return false;
        }

        foreach (var (key, value) in before)
        {
            if (!after.TryGetValue(key, out var afterValue) || afterValue != value)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task<(PipelineRuleRequest? Request, IResult? Error)> ReadRequestAsync(HttpContext http, CancellationToken cancellationToken)
    {
        PipelineRuleRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, PipelineRulesJsonContext.Default.PipelineRuleRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return (null, Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }

        if (request is null)
        {
            return (null, Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest));
        }

        return (request, null);
    }
}
