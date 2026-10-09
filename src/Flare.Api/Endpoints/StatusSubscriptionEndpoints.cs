using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Api.Query;
using Flare.Api.Status;

namespace Flare.Api.Endpoints;

/// <summary>
/// Visitor email subscriptions to a status page (ADR-0162), all unauthenticated: the status page being enabled
/// is the credential for subscribing, and a signed link from an email is the credential for confirming or
/// unsubscribing. Confirm and unsubscribe split into a GET that describes the link and a POST that acts, so a mail
/// scanner that fetches the URL cannot change anything.
/// </summary>
public static class StatusSubscriptionEndpoints
{
    public const string RateLimitPolicy = "StatusSubscribe";

    public static IEndpointRouteBuilder MapStatusSubscriptionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/public/status/{slug}/subscribe", HandleSubscribeAsync).RequireRateLimiting(RateLimitPolicy);
        endpoints.MapGet("/api/public/status/subscriptions/confirm", (string? token, IStatusSubscriptionLinkSigner signer, IStatusPageQueryService pages, CancellationToken ct) =>
            HandleInfoAsync(signer.ValidateConfirm(token), pages, ct));
        endpoints.MapPost("/api/public/status/subscriptions/confirm", HandleConfirmAsync).RequireRateLimiting(RateLimitPolicy);
        endpoints.MapGet("/api/public/status/subscriptions/unsubscribe", (string? token, IStatusSubscriptionLinkSigner signer, IStatusPageQueryService pages, CancellationToken ct) =>
            HandleInfoAsync(signer.ValidateUnsubscribe(token), pages, ct));
        endpoints.MapPost("/api/public/status/subscriptions/unsubscribe", HandleUnsubscribeAsync).RequireRateLimiting(RateLimitPolicy);
        return endpoints;
    }

    /// <summary>Admin routes: see who is subscribed to a page and remove an address.</summary>
    public static IEndpointRouteBuilder MapStatusSubscriberAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/status-pages/{id:guid}/subscribers", async (Guid id, IStatusPageQueryService pages, IStatusSubscriberQueryService subscribers, CancellationToken ct) =>
            await pages.GetAsync(id, ct) is null
                ? Results.NotFound()
                : Results.Json(new StatusSubscriberListResponse(await subscribers.ListAsync(id, ct)), StatusPagesJsonContext.Default.StatusSubscriberListResponse));
        endpoints.MapDelete("/api/status-pages/{id:guid}/subscribers/{subscriberId:guid}", async (Guid id, Guid subscriberId, IStatusSubscriberQueryService subscribers, CancellationToken ct) =>
            await subscribers.DeleteAsync(id, subscriberId, ct) ? Results.NoContent() : Results.NotFound());
        return endpoints;
    }

    private static async Task<IResult> HandleSubscribeAsync(
        string slug,
        HttpContext http,
        IStatusPageQueryService pages,
        IStatusSubscriberQueryService subscribers,
        IStatusSubscriberMailer mailer,
        IStatusSubscriptionLinkSigner signer,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var page = StatusPageRequest.IsValidSlug(slug) ? await pages.GetPublishedBySlugAsync(slug, cancellationToken) : null;
        if (page is null)
        {
            return Results.NotFound();
        }

        if (!mailer.IsAvailable)
        {
            return Results.Problem("Subscriptions are not available: this server has no SMTP server or public URL configured.", statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        var (body, problem) = await ReadAsync(http, StatusPagesJsonContext.Default.StatusSubscribeRequest, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        if (!StatusSubscriptions.TryNormalizeEmail(body!.Email, out var email))
        {
            return Results.Problem("Enter a valid email address.", statusCode: StatusCodes.Status400BadRequest);
        }

        var all = await subscribers.ListAsync(page.Id, cancellationToken);
        var id = StatusSubscriptions.IdFor(page.Id, email);
        var existing = all.FirstOrDefault(s => s.Id == id);
        if (existing is null && all.Count >= StatusSubscriptions.MaxPerPage)
        {
            return Results.Problem("This status page cannot take more subscribers.", statusCode: StatusCodes.Status409Conflict);
        }

        var (save, send) = StatusSubscriptions.Subscribe(existing, page.Id, email, time.GetUtcNow());
        if (save is not null)
        {
            await subscribers.SaveAsync(save, cancellationToken);
        }

        if (send && signer.ConfirmUrl(page.Id, email) is { } url)
        {
            await mailer.SendConfirmationAsync(page, email, url, cancellationToken);
        }

        // The same answer whether the address was new, already subscribed, or still inside the resend cooldown.
        return Results.Accepted();
    }

    private static async Task<IResult> HandleInfoAsync(StatusSubscriptionClaims? claims, IStatusPageQueryService pages, CancellationToken cancellationToken)
    {
        if (claims is not { } c)
        {
            return InvalidLink();
        }

        var title = (await pages.GetAsync(c.PageId, cancellationToken))?.Title ?? "";
        return Results.Json(new StatusSubscriptionInfo(title, c.Email), StatusPagesJsonContext.Default.StatusSubscriptionInfo);
    }

    private static async Task<IResult> HandleConfirmAsync(
        HttpContext http,
        IStatusSubscriptionLinkSigner signer,
        IStatusPageQueryService pages,
        IStatusSubscriberQueryService subscribers,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        var (body, problem) = await ReadAsync(http, StatusPagesJsonContext.Default.StatusSubscriptionTokenRequest, cancellationToken);
        if (problem is not null)
        {
            return problem;
        }

        if (signer.ValidateConfirm(body!.Token) is not { } claims)
        {
            return InvalidLink();
        }

        var existing = await subscribers.GetAsync(claims.PageId, StatusSubscriptions.IdFor(claims.PageId, claims.Email), cancellationToken);
        if (existing is null)
        {
            return Results.Problem("This subscription is no longer waiting for confirmation. Subscribe again from the status page.", statusCode: StatusCodes.Status404NotFound);
        }

        if (!existing.Verified)
        {
            await subscribers.SaveAsync(existing with { Verified = true, UpdatedAt = time.GetUtcNow() }, cancellationToken);
        }

        return await HandleInfoAsync(claims, pages, cancellationToken);
    }

    private static async Task<IResult> HandleUnsubscribeAsync(
        HttpContext http,
        string? token,
        IStatusSubscriptionLinkSigner signer,
        IStatusPageQueryService pages,
        IStatusSubscriberQueryService subscribers,
        CancellationToken cancellationToken)
    {
        // A mail client's one-click unsubscribe POSTs with the token in the URL and a form body; the page posts it as JSON.
        if (token is null)
        {
            var (body, problem) = await ReadAsync(http, StatusPagesJsonContext.Default.StatusSubscriptionTokenRequest, cancellationToken);
            if (problem is not null)
            {
                return problem;
            }

            token = body!.Token;
        }

        if (signer.ValidateUnsubscribe(token) is not { } claims)
        {
            return InvalidLink();
        }

        // Idempotent: unsubscribing twice, or an address that was removed by an admin, is still a success.
        await subscribers.DeleteAsync(claims.PageId, StatusSubscriptions.IdFor(claims.PageId, claims.Email), cancellationToken);
        return await HandleInfoAsync(claims, pages, cancellationToken);
    }

    private static IResult InvalidLink() =>
        Results.Problem("This link is invalid or has expired.", statusCode: StatusCodes.Status400BadRequest);

    private static async Task<(T? Body, IResult? Problem)> ReadAsync<T>(HttpContext http, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            var body = await ApiSerialization.ReadAsync(http, typeInfo, cancellationToken);
            return body is null ? (null, Results.Problem("Request body is required.", statusCode: StatusCodes.Status400BadRequest)) : (body, null);
        }
        catch (JsonException ex)
        {
            return (null, Results.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest));
        }
    }
}
