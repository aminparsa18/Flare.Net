using System.Security.Cryptography;
using System.Text;
using Flare.Api.Model;
using MimeKit;

namespace Flare.Api.Status;

/// <summary>
/// The pure rules for visitor subscriptions to a status page (ADR-0162): which addresses are acceptable, how a
/// subscriber's id is derived, and when a confirmation email is (re)sent. Free of ClickHouse and SMTP so they can
/// be unit-tested.
/// </summary>
public static class StatusSubscriptions
{
    /// <summary>The most subscribers (verified or not) one page keeps, so the sign-up form cannot grow the table without bound.</summary>
    public const int MaxPerPage = 2_000;

    /// <summary>How long an unverified address waits before another confirmation email goes to it, so the form cannot be used to mail-bomb someone.</summary>
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(10);

    private const int MaxEmailLength = 254;

    /// <summary>A bare, lower-cased address; false for anything with a display name, whitespace, a group or no dotted domain.</summary>
    public static bool TryNormalizeEmail(string? raw, out string email)
    {
        email = "";
        var candidate = raw?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(candidate) || candidate.Length > MaxEmailLength || candidate.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is '<' or '>' or ',' or ';' or '"'))
        {
            return false;
        }

        if (!MailboxAddress.TryParse(candidate, out var mailbox) || mailbox.Address != candidate)
        {
            return false;
        }

        var domain = candidate[(candidate.LastIndexOf('@') + 1)..];
        if (!domain.Contains('.') || domain.StartsWith('.') || domain.EndsWith('.'))
        {
            return false;
        }

        email = candidate;
        return true;
    }

    /// <summary>Stable per page and address, so subscribing twice is the same row.</summary>
    public static Guid IdFor(Guid pageId, string email) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"{pageId:N}|{email}"))[..16]);

    /// <summary>The opaque handle the public page shows for a component (ADR-0163): stable, per page, and not reversible to the monitor or SLO id.</summary>
    public static Guid ComponentKey(Guid pageId, Guid refId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"component|{pageId:N}|{refId:N}"))[..16]);

    /// <summary>
    /// Validates the component keys a visitor picked. Unknown keys are an error; picking none, or every component,
    /// is normalised to the empty list (= everything), so a component added to the page later is not silently missed.
    /// </summary>
    public static bool TryNormalizeComponents(StatusPage page, IReadOnlyList<Guid>? requested, out IReadOnlyList<Guid> components)
    {
        components = [];
        if (requested is not { Count: > 0 })
        {
            return true;
        }

        var valid = page.Components.Select(c => ComponentKey(page.Id, c.RefId)).ToHashSet();
        var picked = requested.Distinct().ToList();
        if (picked.Any(k => !valid.Contains(k)))
        {
            return false;
        }

        components = picked.Count >= valid.Count ? [] : picked;
        return true;
    }

    /// <summary>
    /// Whether a subscriber hears about an incident touching <paramref name="incidentComponents"/> (page component ref ids).
    /// Everyone hears about an incident that names no component, since nobody can tell whether it concerns them.
    /// </summary>
    public static bool Wants(StatusSubscriber subscriber, Guid pageId, IReadOnlyList<Guid> incidentComponents) =>
        subscriber.Components.Count == 0
        || incidentComponents.Count == 0
        || incidentComponents.Any(refId => subscriber.Components.Contains(ComponentKey(pageId, refId)));

    /// <summary>
    /// What a subscribe request does. <c>Save</c> is the row to write (null: nothing to write) and <c>SendConfirmation</c>
    /// whether to email the confirmation link. A pending row takes the newly picked components; a verified address, or one confirmed-mailed within
    /// <see cref="ResendCooldown"/>, changes nothing - the caller answers the same either way so the form never
    /// reveals who is subscribed.
    /// </summary>
    public static (StatusSubscriber? Save, bool SendConfirmation) Subscribe(StatusSubscriber? existing, Guid pageId, string email, IReadOnlyList<Guid> components, DateTimeOffset now)
    {
        if (existing is { Verified: true })
        {
            return (null, false);
        }

        if (existing is not null && now - existing.UpdatedAt < ResendCooldown)
        {
            return (null, false);
        }

        var row = existing is null
            ? new StatusSubscriber { Id = IdFor(pageId, email), PageId = pageId, Email = email, Components = components, CreatedAt = now, UpdatedAt = now }
            : existing with { Components = components, UpdatedAt = now };
        return (row, true);
    }
}
