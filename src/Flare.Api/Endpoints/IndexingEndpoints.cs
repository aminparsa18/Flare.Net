using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The Indexing page's endpoints: <c>GET /api/indexing/stats</c> and
/// <c>GET /api/indexing/cluster</c>. No query params on either - see
/// <see cref="IndexingQueryService"/>/<see cref="ClusterQueryService"/>'s remarks for why
/// there's no filter to accept. <c>GET /api/indexing/promoted-attributes</c> lists promoted
/// attribute columns (ADR-0062, ADR-0063) for any authenticated user; promoting/demoting one
/// is schema DDL on the whole <c>logs</c>/<c>spans</c> table, so those live in
/// <see cref="MapPromotedAttributeAdminEndpoints"/>, mapped onto Program.cs's Admin-only group.
/// </summary>
public static class IndexingEndpoints
{
    public static IEndpointRouteBuilder MapIndexingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/indexing/stats", HandleGetStatsAsync);
        endpoints.MapGet("/api/indexing/cluster", HandleGetClusterStatusAsync);
        endpoints.MapGet("/api/indexing/promoted-attributes", HandleListPromotedAsync);
        return endpoints;
    }

    public static IEndpointRouteBuilder MapPromotedAttributeAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/indexing/promoted-attributes", HandlePromoteAsync);
        endpoints.MapDelete("/api/indexing/promoted-attributes/{columnName}", HandleDemoteAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleListPromotedAsync(
        HttpContext http,
        IPromotedAttributeAdminService service,
        CancellationToken cancellationToken)
    {
        var response = await service.ListAsync(cancellationToken);
        return ApiSerialization.Write(http, response, IndexingJsonContext.Default.PromotedAttributesResponse);
    }

    private static async Task<IResult> HandlePromoteAsync(
        HttpContext http,
        IPromotedAttributeAdminService service,
        CancellationToken cancellationToken)
    {
        PromoteAttributeRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, IndexingJsonContext.Default.PromoteAttributeRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest);
        }

        if (request is null)
        {
            return Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest);
        }

        return await service.PromoteAsync(request, cancellationToken) switch
        {
            PromoteAttributeOutcome.Promoted => Results.NoContent(),
            PromoteAttributeOutcome.AlreadyPromoted => Results.Problem($"'{request.Key}' is already promoted.", statusCode: StatusCodes.Status409Conflict),
            PromoteAttributeOutcome.NameConflict => Results.Problem(
                $"Column '{PromotedAttributeColumns.ColumnNameFor(request.Table, request.Bag, request.Key)}' is already used by another promoted key.",
                statusCode: StatusCodes.Status409Conflict),
            PromoteAttributeOutcome.LimitReached => Results.Problem(
                $"At most {PromotedAttributeColumns.MaxPromotedAttributes} attributes can be promoted per table; demote one first.",
                statusCode: StatusCodes.Status409Conflict),
            _ => Results.Problem(
                $"Key must be 1-{PromotedAttributeColumns.MaxKeyLength} characters of letters, digits, and . _ - : / @.",
                statusCode: StatusCodes.Status400BadRequest),
        };
    }

    /// <summary>
    /// <c>?table=spans</c> selects the <c>spans</c> table (ADR-0063); omitted means <c>logs</c>,
    /// the only table before that. Needed because <c>attr_res_*</c>/<c>attr_scope_*</c> names
    /// can exist on both tables at once.
    /// </summary>
    private static async Task<IResult> HandleDemoteAsync(
        string columnName,
        string? table,
        IPromotedAttributeAdminService service,
        CancellationToken cancellationToken)
    {
        var parsedTable = PromotedAttributeTable.Logs;
        if (!string.IsNullOrEmpty(table) && (!Enum.TryParse(table, ignoreCase: true, out parsedTable) || !Enum.IsDefined(parsedTable)))
        {
            return Results.Problem("table must be 'logs' or 'spans'.", statusCode: StatusCodes.Status400BadRequest);
        }

        return await service.DemoteAsync(parsedTable, columnName, cancellationToken) ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> HandleGetStatsAsync(
        HttpContext http,
        IIndexingQueryService queryService,
        CancellationToken cancellationToken)
    {
        var response = await queryService.GetStatsAsync(cancellationToken);
        return ApiSerialization.Write(http, response, IndexingJsonContext.Default.IndexingStatsResponse);
    }

    private static async Task<IResult> HandleGetClusterStatusAsync(
        HttpContext http,
        IClusterStatusService clusterStatusService,
        CancellationToken cancellationToken)
    {
        var response = await clusterStatusService.GetStatusAsync(cancellationToken);
        return ApiSerialization.Write(http, response, IndexingJsonContext.Default.ClusterStatusResponse);
    }
}
