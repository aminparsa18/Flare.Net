using System.Text.Json;
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

        var rule = await rules.CreateAsync(request, cancellationToken);
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

        var rule = await rules.UpdateAsync(id, request, cancellationToken);
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
