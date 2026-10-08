using System.Text.Json;
using Flare.Api.Auditing;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;

namespace Flare.Api.Endpoints;

/// <summary>
/// The shared alert-notification-template API: CRUD under <c>/api/alert-templates</c> - same
/// shape as <see cref="MaintenanceWindowEndpoints"/>. Deleting a template that rules still
/// reference is refused with a 409 listing them. See
/// <c>docs-internal/adr/0148-shared-alert-notification-templates.md</c>.
/// </summary>
public static class AlertTemplateEndpoints
{
    public static IEndpointRouteBuilder MapAlertTemplateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/alert-templates", HandleCreateAsync);
        endpoints.MapGet("/api/alert-templates", HandleListAsync);
        endpoints.MapGet("/api/alert-templates/{id:guid}", HandleGetAsync);
        endpoints.MapPut("/api/alert-templates/{id:guid}", HandleUpdateAsync);
        endpoints.MapDelete("/api/alert-templates/{id:guid}", HandleDeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(HttpContext http, IAlertTemplateQueryService templates, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadRequestAsync(http, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        if (NameUniqueness.Conflict((await templates.ListAsync(cancellationToken)).Select(t => (t.Id, t.Name)), "notification template", request!.Name) is { } taken)
        {
            return taken;
        }

        var template = await templates.CreateAsync(request!, cancellationToken);
        AuditContext.SetResourceId(http, template.Id);
        return ApiSerialization.Write(http, template, AlertTemplatesJsonContext.Default.AlertTemplate, statusCode: StatusCodes.Status201Created);
    }

    private static async Task<IResult> HandleListAsync(HttpContext http, IAlertTemplateQueryService templates, CancellationToken cancellationToken) =>
        ApiSerialization.Write(http, await templates.ListAsync(cancellationToken), AlertTemplatesJsonContext.Default.IReadOnlyListAlertTemplate);

    private static async Task<IResult> HandleGetAsync(Guid id, HttpContext http, IAlertTemplateQueryService templates, CancellationToken cancellationToken)
    {
        var template = await templates.GetAsync(id, cancellationToken);
        return template is null ? Results.NotFound() : ApiSerialization.Write(http, template, AlertTemplatesJsonContext.Default.AlertTemplate);
    }

    private static async Task<IResult> HandleUpdateAsync(Guid id, HttpContext http, IAlertTemplateQueryService templates, CancellationToken cancellationToken)
    {
        var (request, problem) = await ReadRequestAsync(http, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        var before = await templates.GetAsync(id, cancellationToken);
        if (NameUniqueness.Conflict((await templates.ListAsync(cancellationToken)).Select(t => (t.Id, t.Name)), "notification template", request!.Name, id, before?.Name) is { } taken)
        {
            return taken;
        }

        var template = await templates.UpdateAsync(id, request!, cancellationToken);
        if (template is null)
        {
            return Results.NotFound();
        }

        AuditContext.SetChange(http, AlertTemplatesJsonContext.Default.AlertTemplate, before, template);
        return ApiSerialization.Write(http, template, AlertTemplatesJsonContext.Default.AlertTemplate);
    }

    private static async Task<IResult> HandleDeleteAsync(Guid id, IAlertTemplateQueryService templates, IAlertQueryService alerts, CancellationToken cancellationToken)
    {
        var users = (await alerts.ListAsync(cancellationToken)).Where(r => r.NotificationTemplateId == id).Select(r => r.Name).Order().ToList();
        if (users.Count > 0)
        {
            return Results.Problem(
                $"The template is used by {users.Count} alert rule(s): {string.Join(", ", users)}. Pick another template on them first.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return await templates.DeleteAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<(AlertTemplateRequest? Request, IResult? Problem)> ReadRequestAsync(HttpContext http, CancellationToken cancellationToken)
    {
        AlertTemplateRequest? request;
        try
        {
            request = await ApiSerialization.ReadAsync(http, AlertTemplatesJsonContext.Default.AlertTemplateRequest, cancellationToken);
        }
        catch (JsonException ex)
        {
            return (null, Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }

        if (request is null)
        {
            return (null, Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest));
        }

        return request.Validate() is { } error
            ? (null, Results.Problem(error, statusCode: StatusCodes.Status400BadRequest))
            : (request, null);
    }
}
