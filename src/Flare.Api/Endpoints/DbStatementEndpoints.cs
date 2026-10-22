using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// Per-statement database view: <c>POST /api/traces/db-statements</c> ranks normalized
/// database statements by total time, p95, call count or errors over a window. POST for the
/// body, same convention as <see cref="NPlusOneEndpoints"/>.
/// </summary>
public static class DbStatementEndpoints
{
    public static IEndpointRouteBuilder MapDbStatementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/traces/db-statements", HandleAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext http,
        IDbStatementQueryService queryService,
        CancellationToken cancellationToken)
    {
        DbStatementRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, DbStatementJsonContext.Default.DbStatementRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        var response = await queryService.GetStatementsAsync(request ?? new DbStatementRequest(), cancellationToken);
        return ApiSerialization.Write(http, response, DbStatementJsonContext.Default.DbStatementResponse);
    }
}
