using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>The pipeline-rule API: field-extraction/redaction rule CRUD under <c>/api/pipeline-rules</c>.</summary>
/// <remarks>
/// Mirrors <see cref="AlertEndpoints"/>'s shape: POST/PUT + JSON body, manual
/// <see cref="JsonSerializer.DeserializeAsync"/> against the source-gen
/// <see cref="PipelineRulesJsonContext"/>, <see cref="Results.Problem"/> on 400s. No
/// dry-run/test endpoints yet - see docs-internal/adr/0033-pipeline-rules-extraction-redaction.md's
/// "deferred to Phase 2" note.
/// </remarks>
public static class PipelineRuleEndpoints
{
    public static IEndpointRouteBuilder MapPipelineRuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/pipeline-rules", HandleCreateAsync);
        endpoints.MapGet("/api/pipeline-rules", HandleListAsync);
        endpoints.MapGet("/api/pipeline-rules/{id:guid}", HandleGetAsync);
        endpoints.MapPut("/api/pipeline-rules/{id:guid}", HandleUpdateAsync);
        endpoints.MapDelete("/api/pipeline-rules/{id:guid}", HandleDeleteAsync);
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
