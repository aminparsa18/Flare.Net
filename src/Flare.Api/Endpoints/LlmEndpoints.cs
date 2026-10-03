using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Identity.LlmPrices;

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

    /// <summary>
    /// <c>PUT/DELETE /api/llm/prices</c> - the Admin-only price overrides behind the estimated
    /// cost (<c>Program.cs</c>'s <c>adminRoutes</c>, same reasoning as
    /// <see cref="MetricMetadataOverrideEndpoints"/>). The model travels in the body/query string
    /// because model names can contain <c>/</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapLlmPriceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/llm/prices", HandleSetPriceAsync);
        endpoints.MapDelete("/api/llm/prices", HandleResetPriceAsync);
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

    private static async Task<IResult> HandleSetPriceAsync(
        HttpContext http,
        ILlmModelPriceStore store,
        CancellationToken cancellationToken)
    {
        SetLlmModelPriceRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, LlmJsonContext.Default.SetLlmModelPriceRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("A request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (LlmPricing.Validate(request, out var normalized) is { } error)
        {
            return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        await store.SetAsync(normalized, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> HandleResetPriceAsync(
        string? model,
        ILlmModelPriceStore store,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return Results.Problem("model is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        await store.ResetAsync(model.Trim(), cancellationToken);
        return Results.NoContent();
    }
}
