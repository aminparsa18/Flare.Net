using System.Text.Json;
using Flare.Api.Auditing;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>Metric attribute-reduction rule CRUD under <c>/api/metric-attribute-rules</c>. Same shape as <see cref="PipelineRuleEndpoints"/>; see docs-internal/adr/0083-metric-attribute-reduction.md.</summary>
public static class MetricAttributeRuleEndpoints
{
    public static IEndpointRouteBuilder MapMetricAttributeRuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/metric-attribute-rules", HandleCreateAsync);
        endpoints.MapGet("/api/metric-attribute-rules", HandleListAsync);
        endpoints.MapGet("/api/metric-attribute-rules/{id:guid}", HandleGetAsync);
        endpoints.MapPut("/api/metric-attribute-rules/{id:guid}", HandleUpdateAsync);
        endpoints.MapDelete("/api/metric-attribute-rules/{id:guid}", HandleDeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, IMetricAttributeRuleQueryService rules, CancellationToken cancellationToken)
    {
        var (request, error) = await ReadRequestAsync(http, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var rule = await rules.CreateAsync(request!, cancellationToken);
        AuditContext.SetResourceId(http, rule.Id);
        return ApiSerialization.Write(http, rule, MetricAttributeRulesJsonContext.Default.MetricAttributeRule, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAsync(HttpContext http, IMetricAttributeRuleQueryService rules, CancellationToken cancellationToken)
    {
        var list = await rules.ListAsync(cancellationToken);
        return ApiSerialization.Write(http, new MetricAttributeRuleListResponse { Rules = list }, MetricAttributeRulesJsonContext.Default.MetricAttributeRuleListResponse);
    }

    private static async Task<IResult> HandleGetAsync(Guid id, HttpContext http, IMetricAttributeRuleQueryService rules, CancellationToken cancellationToken)
    {
        var rule = await rules.GetAsync(id, cancellationToken);
        return rule is null ? Results.NotFound() : ApiSerialization.Write(http, rule, MetricAttributeRulesJsonContext.Default.MetricAttributeRule);
    }

    private static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, IMetricAttributeRuleQueryService rules, CancellationToken cancellationToken)
    {
        var (request, error) = await ReadRequestAsync(http, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var before = await rules.GetAsync(id, cancellationToken);
        var rule = await rules.UpdateAsync(id, request!, cancellationToken);
        if (rule is null)
        {
            return Results.NotFound();
        }

        AuditContext.SetChange(http, MetricAttributeRulesJsonContext.Default.MetricAttributeRule, before, rule);
        return ApiSerialization.Write(http, rule, MetricAttributeRulesJsonContext.Default.MetricAttributeRule);
    }

    private static async Task<IResult> HandleDeleteAsync(Guid id, IMetricAttributeRuleQueryService rules, CancellationToken cancellationToken) =>
        await rules.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();

    private static async Task<(MetricAttributeRuleRequest? Request, IResult? Error)> ReadRequestAsync(HttpContext http, CancellationToken cancellationToken)
    {
        MetricAttributeRuleRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, MetricAttributeRulesJsonContext.Default.MetricAttributeRuleRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return (null, Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }

        if (request is null)
        {
            return (null, Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest));
        }

        return request.Validate() is { } validationError
            ? (null, Results.Problem(validationError, statusCode: StatusCodes.Status400BadRequest))
            : (request, null);
    }
}
