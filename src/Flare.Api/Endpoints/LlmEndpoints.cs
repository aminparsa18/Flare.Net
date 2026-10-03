using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The <c>/llm</c> page's Query API: <c>POST /api/llm/models</c> (one row per provider/model).
/// Same POST+body convention as <see cref="MessagingEndpoints"/>. See
/// docs-internal/adr/0100-llm-observability-genai-spans.md.
/// </summary>
public static class LlmEndpoints
{
    public static IEndpointRouteBuilder MapLlmEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/llm/models", HandleGetModelsAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGetModelsAsync(
        HttpContext http,
        ILlmQueryService queryService,
        CancellationToken cancellationToken)
    {
        LlmModelsRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, LlmJsonContext.Default.LlmModelsRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        request ??= new LlmModelsRequest();

        var response = await queryService.GetModelsAsync(request, cancellationToken);
        return ApiSerialization.Write(http, response, LlmJsonContext.Default.LlmModelsResponse);
    }
}
