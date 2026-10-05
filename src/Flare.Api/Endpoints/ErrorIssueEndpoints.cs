using System.Text.Json;
using Flare.Api.Auditing;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// Triage state for the <c>/errors</c> page: <c>GET /api/errors/issues</c> (any authenticated
/// user) and <c>PUT /api/errors/issues</c> (Member/Admin - ignoring a group silences alert
/// rules). See <c>docs-internal/adr/0121-error-issue-lifecycle.md</c>.
/// </summary>
public static class ErrorIssueEndpoints
{
    public static IEndpointRouteBuilder MapErrorIssueReadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/errors/issues", HandleListAsync);
        return endpoints;
    }

    public static IEndpointRouteBuilder MapErrorIssueWriteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/errors/issues", HandleUpsertAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleListAsync(HttpContext http, IErrorIssueQueryService issues, CancellationToken cancellationToken)
    {
        var response = new ErrorIssueListResponse { Issues = await issues.ListAsync(cancellationToken) };
        return ApiSerialization.Write(http, response, ErrorIssuesJsonContext.Default.ErrorIssueListResponse);
    }

    private static async Task<IResult> HandleUpsertAsync(HttpContext http, IErrorIssueQueryService issues, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ErrorIssueRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, ErrorIssuesJsonContext.Default.ErrorIssueRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (request.Validate(timeProvider.GetUtcNow()) is { } error)
        {
            return Results.Problem(error, statusCode: StatusCodes.Status400BadRequest);
        }

        var issue = await issues.UpsertAsync(request, http.User.Identity?.Name ?? "", cancellationToken);
        AuditContext.SetResourceId(http, Errors.ErrorIssueFingerprint.Compute(request.ExceptionType, request.ExceptionMessage));
        // Back to Open and unassigned = no stored state; answer 204 so the caller drops its row.
        return issue is null ? Results.NoContent() : ApiSerialization.Write(http, issue, ErrorIssuesJsonContext.Default.ErrorIssue);
    }
}
