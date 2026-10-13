using System.Text.Json;
using Flare.Api.Auditing;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// Dashboard-controlled runtime settings under <c>/api/settings/pii-redaction</c> (docs-internal/adr/0210-runtime-settings-pii-toggle.md).
/// Reading is open to members, who manage the pipeline rules this sits beside; changing it alters what every
/// future event stores, so <c>Program.cs</c> maps <see cref="MapRuntimeSettingsWriteEndpoints"/> onto the Admin-only group.
/// </summary>
public static class RuntimeSettingsEndpoints
{
    public static IEndpointRouteBuilder MapRuntimeSettingsReadEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/settings/pii-redaction", HandleGetAsync);
        return endpoints;
    }

    public static IEndpointRouteBuilder MapRuntimeSettingsWriteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/settings/pii-redaction", HandleSetAsync);
        endpoints.MapDelete("/api/settings/pii-redaction", HandleClearAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleGetAsync(HttpContext http, IRuntimeSettingsQueryService settings, CancellationToken cancellationToken) =>
        ApiSerialization.Write(http, await settings.GetPiiRedactionAsync(cancellationToken), RuntimeSettingsJsonContext.Default.PiiRedactionSetting);

    private static async Task<IResult> HandleSetAsync(HttpContext http, IRuntimeSettingsQueryService settings, CancellationToken cancellationToken)
    {
        PiiRedactionSettingRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, RuntimeSettingsJsonContext.Default.PiiRedactionSettingRequest, cancellationToken);
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

        var before = await settings.GetPiiRedactionAsync(cancellationToken);
        var saved = await settings.SetPiiRedactionAsync(request.Enabled!.Value, http.User.Identity?.Name ?? "", cancellationToken);
        AuditContext.SetChange(http, RuntimeSettingsJsonContext.Default.PiiRedactionSetting, before, saved);
        return ApiSerialization.Write(http, saved, RuntimeSettingsJsonContext.Default.PiiRedactionSetting);
    }

    private static async Task<IResult> HandleClearAsync(HttpContext http, IRuntimeSettingsQueryService settings, CancellationToken cancellationToken)
    {
        var before = await settings.GetPiiRedactionAsync(cancellationToken);
        if (!await settings.ClearPiiRedactionAsync(http.User.Identity?.Name ?? "", cancellationToken))
        {
            return Results.NotFound();
        }

        AuditContext.SetChange(http, RuntimeSettingsJsonContext.Default.PiiRedactionSetting, before, new PiiRedactionSetting());
        return Results.NoContent();
    }
}
