using System.Security.Claims;
using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Retention;

namespace Flare.Api.Endpoints;

/// <summary>
/// Data retention under <c>/api/retention</c> (docs-internal/adr/0143-retention-ttl.md). Reading is
/// open to any member, so the dashboard can show it; changing it deletes data, so
/// <c>Program.cs</c> maps <see cref="MapRetentionWriteEndpoints"/> onto the Admin-only group.
/// </summary>
public static class RetentionEndpoints
{
    public static IEndpointRouteBuilder MapRetentionReadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/retention", HandleGetAsync);
        return endpoints;
    }

    public static IEndpointRouteBuilder MapRetentionWriteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/retention", HandleSetAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGetAsync(HttpContext http, IRetentionService retention, CancellationToken cancellationToken) =>
        ApiSerialization.Write(http, await retention.GetAsync(cancellationToken), RetentionJsonContext.Default.RetentionResponse);

    private static async Task<IResult> HandleSetAsync(HttpContext http, ClaimsPrincipal principal, IRetentionService retention, CancellationToken cancellationToken)
    {
        SetRetentionRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, RetentionJsonContext.Default.SetRetentionRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Validate() is { } validationError)
        {
            return Results.Problem(validationError, statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var transactionId = await retention.RequestAsync(request.Signals!, request.ColdAfterDays, request.Rules, principal.Identity?.Name ?? "", cancellationToken);
            return ApiSerialization.Write(http, new SetRetentionResponse { TransactionId = transactionId }, RetentionJsonContext.Default.SetRetentionResponse, StatusCodes.Status202Accepted);
        }
        catch (ColdStorageUnavailableException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }
        catch (RetentionBusyException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }
}
