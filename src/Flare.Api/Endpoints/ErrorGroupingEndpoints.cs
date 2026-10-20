using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// Grouping rules for the <c>/errors</c> page: <c>GET /api/errors/grouping-rules</c> (any authenticated user) and <c>PUT</c>
/// (Member/Admin) which replaces the whole configuration and re-keys triage state to the new groups. See
/// <c>docs-internal/adr/0259-error-grouping-rules.md</c>.
/// </summary>
public static class ErrorGroupingEndpoints
{
    public static IEndpointRouteBuilder MapErrorGroupingReadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/errors/grouping-rules", HandleGetAsync);
        return endpoints;
    }

    public static IEndpointRouteBuilder MapErrorGroupingWriteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/errors/grouping-rules", HandlePutAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGetAsync(HttpContext http, IErrorGroupingQueryService grouping, CancellationToken cancellationToken) =>
        ApiSerialization.Write(http, await grouping.GetDocumentAsync(cancellationToken), ErrorGroupingJsonContext.Default.ErrorGroupingDocument);

    private static async Task<IResult> HandlePutAsync(HttpContext http, IErrorGroupingQueryService grouping, IErrorIssueQueryService issues, CancellationToken cancellationToken)
    {
        ErrorGroupingDocument? document;
        try
        {
            document = await ApiSerialization.ReadAsync(http, ErrorGroupingJsonContext.Default.ErrorGroupingDocument, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (document is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        document = document.Sanitized();
        var error = document.Validate() ?? await grouping.ValidateAsync(document, cancellationToken);
        if (error is not null)
        {
            return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        await grouping.ReplaceAsync(document, http.User.Identity?.Name ?? "", cancellationToken);
        // Triage state is keyed by group, so it follows the groups to their new keys.
        await issues.RekeyAsync(cancellationToken);
        return ApiSerialization.Write(http, await grouping.GetDocumentAsync(cancellationToken), ErrorGroupingJsonContext.Default.ErrorGroupingDocument);
    }
}
