using System.Text.Json;
using Flare.Api.Ai;
using Flare.Api.Auditing;
using Flare.Api.Json;
using Flare.Api.Model;

namespace Flare.Api.Endpoints;

/// <summary>Opt-in AI actions - see docs-internal/adr/0103-explain-exception-llm.md. Any signed-in user.</summary>
public static class AiEndpoints
{
    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/ai/status", HandleStatus);
        endpoints.MapPost("/api/ai/explain-exception", HandleExplainAsync);
        endpoints.MapPost("/api/ai/nl-filter", HandleNlFilterAsync);
        return endpoints;
    }

    private static IResult HandleStatus(HttpContext http, IExceptionExplainService explainer) =>
        ApiSerialization.Write(http, new AiStatusResponse { Enabled = explainer.IsEnabled }, AiJsonContext.Default.AiStatusResponse);

    private static async Task<IResult> HandleExplainAsync(HttpContext http, IExceptionExplainService explainer, CancellationToken cancellationToken)
    {
        if (!explainer.IsEnabled)
        {
            return Results.Problem("AI features are not enabled on this Flare instance.", statusCode: StatusCodes.Status404NotFound);
        }

        ExplainExceptionRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AiJsonContext.Default.ExplainExceptionRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        AuditContext.SetResourceId(http, request.ServiceName);
        var (result, error) = await explainer.ExplainAsync(request, cancellationToken);
        return result is null
            ? Results.Problem(error, statusCode: StatusCodes.Status502BadGateway)
            : ApiSerialization.Write(http, result, AiJsonContext.Default.ExplainExceptionResponse);
    }

    private static async Task<IResult> HandleNlFilterAsync(HttpContext http, INlFilterService service, CancellationToken cancellationToken)
    {
        if (!service.IsEnabled)
        {
            return Results.Problem("AI features are not enabled on this Flare instance.", statusCode: StatusCodes.Status404NotFound);
        }

        NlFilterRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AiJsonContext.Default.NlFilterRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        AuditContext.SetResourceId(http, request.Target);
        var (result, error) = await service.GenerateAsync(request, cancellationToken);
        return result is null
            ? Results.Problem(error, statusCode: StatusCodes.Status502BadGateway)
            : ApiSerialization.Write(http, result, AiJsonContext.Default.NlFilterResponse);
    }
}
