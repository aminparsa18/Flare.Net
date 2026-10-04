using System.Security.Claims;
using System.Text.Json;
using Flare.Identity.UserPreferences;

namespace Flare.Api.Endpoints;

/// <summary>
/// Per-user UI preferences under <c>/api/me/preferences/{key}</c> (ADR-0110), so settings like
/// Appearance follow a user across browsers. The value is an opaque JSON object the dashboard
/// owns; the server only checks it is a JSON object within <see cref="MaxBytes"/> and that the
/// key is one it knows. Self-service: any authenticated user (Viewer included) may set their own.
/// </summary>
public static class UserPreferencesEndpoints
{
    internal const int MaxBytes = 4096;

    /// <summary>Preference groups the dashboard may store. An allow-list keeps the table from becoming arbitrary storage.</summary>
    internal static readonly string[] AllowedKeys = ["appearance", "regional", "explorer", "keyboard", "notifications"];

    public static IEndpointRouteBuilder MapUserPreferencesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/me/preferences/{key}", HandleGetAsync);
        endpoints.MapPut("/api/me/preferences/{key}", HandlePutAsync);
        endpoints.MapDelete("/api/me/preferences/{key}", HandleDeleteAsync);
        return endpoints;
    }

    internal static async Task<IResult> HandleGetAsync(string key, ClaimsPrincipal principal, IUserPreferencesStore store, CancellationToken cancellationToken)
    {
        if (!AllowedKeys.Contains(key))
        {
            return Results.NotFound();
        }

        var json = await store.GetAsync(OwnerId(principal), key, cancellationToken);
        return json is null ? Results.NoContent() : Results.Content(json, "application/json");
    }

    internal static async Task<IResult> HandlePutAsync(string key, HttpContext http, ClaimsPrincipal principal, IUserPreferencesStore store, CancellationToken cancellationToken)
    {
        if (!AllowedKeys.Contains(key))
        {
            return Results.NotFound();
        }

        using var reader = new StreamReader(http.Request.Body);
        var buffer = new char[MaxBytes + 1];
        var read = await reader.ReadBlockAsync(buffer, cancellationToken);
        if (read > MaxBytes)
        {
            return Results.Problem(title: "Preferences too large.", detail: $"Limit is {MaxBytes} characters.", statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        var json = new string(buffer, 0, read);
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Results.Problem(title: "Invalid preferences.", detail: "Body must be a JSON object.", statusCode: StatusCodes.Status400BadRequest);
            }
        }
        catch (JsonException)
        {
            return Results.Problem(title: "Invalid preferences.", detail: "Body is not valid JSON.", statusCode: StatusCodes.Status400BadRequest);
        }

        await store.SetAsync(OwnerId(principal), key, json, cancellationToken);
        return Results.NoContent();
    }

    internal static async Task<IResult> HandleDeleteAsync(string key, ClaimsPrincipal principal, IUserPreferencesStore store, CancellationToken cancellationToken)
    {
        if (!AllowedKeys.Contains(key))
        {
            return Results.NotFound();
        }

        await store.DeleteAsync(OwnerId(principal), key, cancellationToken);
        return Results.NoContent();
    }

    /// <summary>The caller's user id, or <see cref="Guid.Empty"/> when Flare's opt-in auth is off - preferences are then shared, the only identity there is.</summary>
    private static Guid OwnerId(ClaimsPrincipal principal) =>
        principal.Identity is { IsAuthenticated: true } && Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : Guid.Empty;
}
