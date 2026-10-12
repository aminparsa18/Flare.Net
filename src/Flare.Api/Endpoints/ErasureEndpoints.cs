using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Flare.Api.Auditing;
using Flare.Api.Erasure;
using Flare.Api.Json;

namespace Flare.Api.Endpoints;

/// <summary>
/// Data-subject erasure and export under <c>/api/erasure</c> (docs-internal/adr/0198-data-subject-erasure-export.md). Everything
/// here reads or deletes personal data across every service, so <c>Program.cs</c> maps it onto the Admin-only group.
/// </summary>
public static class ErasureEndpoints
{
    /// <summary>Audit entry shape: which attributes were matched and a hash of the values, never the values (they are the personal data).</summary>
    /// <remarks>The attribute list is called <c>attributes</c>, not <c>keys</c>: the audit diff redacts anything named like a secret.</remarks>
    private sealed record ErasureAuditChange(string[] Attributes, string SubjectHash, long Rows);

    /// <summary>Export audits what was asked for; the row count is only known after streaming, so it is left out.</summary>
    private sealed record ExportAuditChange(string[] Attributes, string SubjectHash);

    public static IEndpointRouteBuilder MapErasureEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/erasure/preview", HandlePreviewAsync);
        endpoints.MapPost("/api/erasure/export", HandleExportAsync);
        endpoints.MapPost("/api/erasure", HandleStartAsync);
        endpoints.MapGet("/api/erasure", HandleListAsync);
        endpoints.MapGet("/api/erasure/{id:guid}", HandleGetAsync);
        return endpoints;
    }

    private static async Task<IResult> HandlePreviewAsync(HttpContext http, IErasureService erasure, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadAsync(http, requireConfirm: false, cancellationToken);
        return problem ?? ApiSerialization.Write(http, await erasure.PreviewAsync(request!.Identifiers!, cancellationToken), ErasureJsonContext.Default.ErasurePreviewResponse);
    }

    private static async Task<IResult> HandleStartAsync(HttpContext http, ClaimsPrincipal principal, IErasureService erasure, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadAsync(http, requireConfirm: true, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        try
        {
            var transactionId = await erasure.StartAsync(request!.Identifiers!, principal.Identity?.Name ?? "", cancellationToken);
            var started = await erasure.GetAsync(transactionId, cancellationToken);
            AuditContext.SetResourceId(http, transactionId);
            AuditContext.SetChange(http, null, new ErasureAuditChange(
                request.Identifiers!.Select(i => i.Key).Distinct().ToArray(),
                ErasureSql.SubjectHash(request.Identifiers!),
                started?.Tables.Sum(t => t.Rows) ?? 0));
            return ApiSerialization.Write(http, new StartErasureResponse { TransactionId = transactionId }, ErasureJsonContext.Default.StartErasureResponse, StatusCodes.Status202Accepted);
        }
        catch (ErasureBusyException ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
        }
    }

    /// <summary>
    /// Streams NDJSON. Not buffered into one response object: a subject with a year of logs is far too large for that.
    /// </summary>
    private static async Task<IResult> HandleExportAsync(HttpContext http, IErasureService erasure, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadAsync(http, requireConfirm: false, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        AuditContext.SetChange(http, null, new ExportAuditChange(
            request!.Identifiers!.Select(i => i.Key).Distinct().ToArray(),
            ErasureSql.SubjectHash(request.Identifiers!)));

        http.Response.ContentType = "application/x-ndjson";
        http.Response.Headers.ContentDisposition = "attachment; filename=\"flare-subject-export.ndjson\"";
        await foreach (var line in erasure.ExportAsync(request.Identifiers!, cancellationToken))
        {
            await http.Response.WriteAsync(line + "\n", Encoding.UTF8, cancellationToken);
        }

        return Results.Empty;
    }

    private static async Task<IResult> HandleListAsync(HttpContext http, IErasureService erasure, CancellationToken cancellationToken) =>
        ApiSerialization.Write(http, new ErasureListResponse { Operations = await erasure.ListAsync(cancellationToken) }, ErasureJsonContext.Default.ErasureListResponse);

    private static async Task<IResult> HandleGetAsync(HttpContext http, Guid id, IErasureService erasure, CancellationToken cancellationToken) =>
        await erasure.GetAsync(id, cancellationToken) is { } operation
            ? ApiSerialization.Write(http, operation, ErasureJsonContext.Default.ErasureOperation)
            : Results.NotFound();

    private static async Task<(ErasureRequest? Request, IResult? Problem)> ReadAsync(HttpContext http, bool requireConfirm, CancellationToken cancellationToken)
    {
        ErasureRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, ErasureJsonContext.Default.ErasureRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return (null, Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }

        if (request is null)
        {
            return (null, Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest));
        }

        return request.Validate(requireConfirm) is { } error
            ? (null, Results.Problem(error, statusCode: StatusCodes.Status400BadRequest))
            : (request, null);
    }
}
