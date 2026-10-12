using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// Ownership rules for the <c>/errors</c> page: <c>GET /api/errors/ownership-rules</c> (any
/// authenticated user) and <c>PUT</c> (Member/Admin) which replaces the whole ordered list. See
/// <c>docs-internal/adr/0195-error-issue-links-ownership.md</c>.
/// </summary>
public static class ErrorOwnershipEndpoints
{
    public static IEndpointRouteBuilder MapErrorOwnershipReadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/errors/ownership-rules", HandleGetAsync);
        return endpoints;
    }

    public static IEndpointRouteBuilder MapErrorOwnershipWriteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/errors/ownership-rules", HandlePutAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGetAsync(HttpContext http, IErrorOwnershipQueryService ownership, CancellationToken cancellationToken)
    {
        var document = new ErrorOwnershipRulesDocument { Rules = await ownership.ListAsync(cancellationToken) };
        return ApiSerialization.Write(http, document, ErrorOwnershipJsonContext.Default.ErrorOwnershipRulesDocument);
    }

    private static async Task<IResult> HandlePutAsync(HttpContext http, IErrorOwnershipQueryService ownership, CancellationToken cancellationToken)
    {
        ErrorOwnershipRulesDocument? document;
        try
        {
            document = await ApiSerialization.ReadAsync(http, ErrorOwnershipJsonContext.Default.ErrorOwnershipRulesDocument, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (document is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (document.Validate() is { } error)
        {
            return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        await ownership.ReplaceAsync(document.Rules, cancellationToken);
        return ApiSerialization.Write(http, document, ErrorOwnershipJsonContext.Default.ErrorOwnershipRulesDocument);
    }
}
