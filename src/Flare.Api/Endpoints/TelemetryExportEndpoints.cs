using System.Text.Json;
using Flare.Api.Auditing;
using Flare.Api.Export;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// Managed telemetry export under <c>/api/forwarding</c> and <c>/api/archive</c> (docs-internal/adr/0157-managed-telemetry-export.md).
/// Targets and settings carry live credentials, so everything is Admin-only except the archive status, which holds
/// no secrets and feeds the Ingestion page (<see cref="MapTelemetryExportStatusEndpoints"/>).
/// </summary>
public static class TelemetryExportEndpoints
{
    public static IEndpointRouteBuilder MapTelemetryExportAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/forwarding/targets", HandleListAsync);
        endpoints.MapPost("/api/forwarding/targets", HandleCreateAsync);
        endpoints.MapPut("/api/forwarding/targets/{id:guid}", HandleUpdateAsync);
        endpoints.MapDelete("/api/forwarding/targets/{id:guid}", HandleDeleteAsync);
        endpoints.MapGet("/api/forwarding/status", HandleForwardingStatusAsync);
        endpoints.MapGet("/api/archive/settings", HandleGetArchiveAsync);
        endpoints.MapPut("/api/archive/settings", HandleSaveArchiveAsync);
        endpoints.MapDelete("/api/archive/settings", HandleDeleteArchiveAsync);
        return endpoints;
    }

    public static IEndpointRouteBuilder MapTelemetryExportStatusEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/archive/status", HandleArchiveStatusAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleListAsync(HttpContext http, ITelemetryExportQueryService exports, CancellationToken cancellationToken)
    {
        var targets = (await exports.ListForwardingAsync(cancellationToken)).Select(TelemetryExportMasking.Redact).ToList();
        return ApiSerialization.Write(http, new ForwardingTargetListResponse { Targets = targets }, TelemetryExportJsonContext.Default.ForwardingTargetListResponse);
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, ITelemetryExportQueryService exports, CancellationToken cancellationToken)
    {
        var (request, error) = await ReadAsync(http, TelemetryExportJsonContext.Default.ForwardingTargetRequest, r => r.Validate(), cancellationToken);
        if (error is not null)
        {
            return error;
        }

        if (NameUniqueness.Conflict((await exports.ListForwardingAsync(cancellationToken)).Select(t => (t.Id, t.Name)), "forwarding target", request!.Name) is { } taken)
        {
            return taken;
        }

        var target = await exports.CreateForwardingAsync(request!, cancellationToken);
        AuditContext.SetResourceId(http, target.Id);
        return ApiSerialization.Write(http, TelemetryExportMasking.Redact(target), TelemetryExportJsonContext.Default.ForwardingTarget, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, ITelemetryExportQueryService exports, CancellationToken cancellationToken)
    {
        var (request, error) = await ReadAsync(http, TelemetryExportJsonContext.Default.ForwardingTargetRequest, r => r.Validate(), cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var before = await exports.GetForwardingAsync(id, cancellationToken);
        if (NameUniqueness.Conflict((await exports.ListForwardingAsync(cancellationToken)).Select(t => (t.Id, t.Name)), "forwarding target", request!.Name, id, before?.Name) is { } taken)
        {
            return taken;
        }

        var target = await exports.UpdateForwardingAsync(id, request!, cancellationToken);
        if (target is null)
        {
            return Results.NotFound();
        }

        // Snapshots carry masked headers so the audit trail never stores a credential.
        AuditContext.SetChange(http, TelemetryExportJsonContext.Default.ForwardingTarget,
            before is null ? null : TelemetryExportMasking.Redact(before), TelemetryExportMasking.Redact(target));
        return ApiSerialization.Write(http, TelemetryExportMasking.Redact(target), TelemetryExportJsonContext.Default.ForwardingTarget);
    }

    private static async Task<IResult> HandleDeleteAsync(Guid id, ITelemetryExportQueryService exports, CancellationToken cancellationToken) =>
        await exports.DeleteForwardingAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();

    private static async Task<IResult> HandleForwardingStatusAsync(HttpContext http, IExportStatusStore status, CancellationToken cancellationToken) =>
        ApiSerialization.Write(http, await status.ReadForwardingAsync(cancellationToken), TelemetryExportJsonContext.Default.ForwardingStatusResponse);

    private static async Task<IResult> HandleGetArchiveAsync(HttpContext http, ITelemetryExportQueryService exports, CancellationToken cancellationToken)
    {
        var settings = await exports.GetArchiveAsync(cancellationToken) ?? new ArchiveSettings();
        return ApiSerialization.Write(http, TelemetryExportMasking.Redact(settings), TelemetryExportJsonContext.Default.ArchiveSettings);
    }

    private static async Task<IResult> HandleSaveArchiveAsync(HttpContext http, ITelemetryExportQueryService exports, CancellationToken cancellationToken)
    {
        var (request, error) = await ReadAsync(http, TelemetryExportJsonContext.Default.ArchiveSettingsRequest, r => r.Validate(), cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var before = await exports.GetArchiveAsync(cancellationToken);
        var accessKey = TelemetryExportMasking.Restore(request!.AccessKey, before?.AccessKey ?? "") ?? before?.AccessKey ?? "";
        var secretKey = TelemetryExportMasking.Restore(request.SecretKey, before?.SecretKey ?? "") ?? before?.SecretKey ?? "";
        if ((request.Enabled ?? true) && (string.IsNullOrWhiteSpace(accessKey) || string.IsNullOrWhiteSpace(secretKey)))
        {
            return Results.Problem("Access key and secret key are required to enable the archive.", statusCode: StatusCodes.Status400BadRequest);
        }

        var settings = await exports.SaveArchiveAsync(request, cancellationToken);
        AuditContext.SetChange(http, TelemetryExportJsonContext.Default.ArchiveSettings,
            before is null ? null : TelemetryExportMasking.Redact(before), TelemetryExportMasking.Redact(settings));
        return ApiSerialization.Write(http, TelemetryExportMasking.Redact(settings), TelemetryExportJsonContext.Default.ArchiveSettings);
    }

    private static async Task<IResult> HandleDeleteArchiveAsync(ITelemetryExportQueryService exports, CancellationToken cancellationToken) =>
        await exports.DeleteArchiveAsync(cancellationToken) ? Results.NoContent() : Results.NotFound();

    private static async Task<IResult> HandleArchiveStatusAsync(HttpContext http, IExportStatusStore status, CancellationToken cancellationToken) =>
        ApiSerialization.Write(http, await status.ReadArchiveAsync(cancellationToken), TelemetryExportJsonContext.Default.ArchiveStatusResponse);

    private static async Task<(T? Request, IResult? Error)> ReadAsync<T>(
        HttpContext http, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, Func<T, string?> validate, CancellationToken cancellationToken)
        where T : class
    {
        T? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, typeInfo, cancellationToken);
        }
        catch (JsonException ex)
        {
            return (null, Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }

        if (request is null)
        {
            return (null, Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest));
        }

        return validate(request) is { } validationError
            ? (null, Results.Problem(validationError, statusCode: StatusCodes.Status400BadRequest))
            : (request, null);
    }
}
