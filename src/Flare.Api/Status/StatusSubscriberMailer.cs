using Flare.Api.Alerting;
using Flare.Api.Model;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Flare.Api.Status;

/// <summary>One verified subscriber, the link that removes them and, when the page has components to choose from, the link that edits their selection.</summary>
public sealed record StatusMailRecipient(string Email, string UnsubscribeUrl, string? PreferencesUrl = null);

/// <summary>
/// Sends the two emails a visitor subscription needs (ADR-0162): the confirmation link, and each incident
/// update. Uses the same SMTP server as email alert channels (<see cref="EmailOptions"/>).
/// </summary>
public interface IStatusSubscriberMailer
{
    /// <summary>True when visitors can subscribe: SMTP and a public dashboard URL (the base of every link) are both configured.</summary>
    bool IsAvailable { get; }

    Task<NotificationResult> SendConfirmationAsync(StatusPage page, string email, string confirmUrl, CancellationToken cancellationToken);

    /// <summary>One message per recipient over one connection, each carrying its own unsubscribe link. Returns how many were not delivered.</summary>
    Task<int> SendIncidentAsync(StatusPage page, StatusIncident incident, StatusIncidentUpdate update, IReadOnlyList<string> components, IReadOnlyList<StatusMailRecipient> recipients, CancellationToken cancellationToken);
}

public sealed class StatusSubscriberMailer(IOptions<EmailOptions> email, IOptions<AlertLinkOptions> links, ILogger<StatusSubscriberMailer> logger) : IStatusSubscriberMailer
{
    public bool IsAvailable => !string.IsNullOrWhiteSpace(email.Value.Host) && !string.IsNullOrWhiteSpace(email.Value.From) && !string.IsNullOrWhiteSpace(links.Value.PublicUrl);

    public async Task<NotificationResult> SendConfirmationAsync(StatusPage page, string address, string confirmUrl, CancellationToken cancellationToken)
    {
        try
        {
            var failed = await SendAsync([BuildConfirmation(email.Value.From, page, address, confirmUrl)], cancellationToken);
            return failed == 0 ? new NotificationResult(true, 0, null) : new NotificationResult(false, 0, "The SMTP server rejected the recipient.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Status page '{Slug}' confirmation email failed", page.Slug);
            return new NotificationResult(false, 0, ex.Message);
        }
    }

    public async Task<int> SendIncidentAsync(StatusPage page, StatusIncident incident, StatusIncidentUpdate update, IReadOnlyList<string> components, IReadOnlyList<StatusMailRecipient> recipients, CancellationToken cancellationToken)
    {
        if (recipients.Count == 0)
        {
            return 0;
        }

        var publicUrl = links.Value.PublicUrl;
        var messages = recipients.Select(r => BuildIncident(email.Value.From, page, incident, update, components, publicUrl, r)).ToList();
        try
        {
            return await SendAsync(messages, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Status page '{Slug}' incident email failed", page.Slug);
            return recipients.Count;
        }
    }

    /// <summary>Sends every message over one connection; a rejected recipient is counted and skipped. Returns how many failed.</summary>
    private async Task<int> SendAsync(IReadOnlyList<MimeMessage> messages, CancellationToken cancellationToken)
    {
        var opts = email.Value;
        using var client = new SmtpClient();
        await client.ConnectAsync(opts.Host, opts.Port, opts.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, cancellationToken);
        if (!string.IsNullOrWhiteSpace(opts.Username))
        {
            await client.AuthenticateAsync(opts.Username, opts.Password, cancellationToken);
        }

        var failed = 0;
        foreach (var message in messages)
        {
            try
            {
                await client.SendAsync(message, cancellationToken);
            }
            catch (Exception ex) when (ex is SmtpCommandException or SmtpProtocolException)
            {
                failed++;
                logger.LogWarning(ex, "Status page email to a subscriber was rejected");
            }
        }

        await client.DisconnectAsync(true, cancellationToken);
        return failed;
    }

    internal static MimeMessage BuildConfirmation(string from, StatusPage page, string address, string confirmUrl)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(address));
        message.Subject = $"Confirm your subscription to {page.Title}";
        message.Body = new TextPart("plain")
        {
            Text = $"Someone asked for incident updates about {page.Title} to be sent to this address.\n\n"
                 + $"To start receiving them, confirm here:\n{confirmUrl}\n\n"
                 + "If this wasn't you, ignore this email and nothing will be sent.\n",
        };
        return message;
    }

    internal static MimeMessage BuildIncident(string from, StatusPage page, StatusIncident incident, StatusIncidentUpdate update, IReadOnlyList<string> components, string publicUrl, StatusMailRecipient recipient)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        message.To.Add(MailboxAddress.Parse(recipient.Email));
        message.Subject = $"[{page.Title}] {update.Status}: {incident.Title}";
        message.Headers.Add("List-Unsubscribe", $"<{recipient.UnsubscribeUrl}>");
        message.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");

        var lines = new List<string> { update.Message };
        if (components.Count > 0)
        {
            lines.Add("Affected: " + string.Join(", ", components));
        }

        lines.Add($"{publicUrl.TrimEnd('/')}/status/{page.Slug}");
        var footer = $"You get this because you subscribed to {page.Title}. Unsubscribe: {recipient.UnsubscribeUrl}";
        if (recipient.PreferencesUrl is not null)
        {
            footer += $"\nChoose which components you hear about: {recipient.PreferencesUrl}";
        }

        lines.Add(footer);
        message.Body = new TextPart("plain") { Text = string.Join("\n\n", lines) + "\n" };
        return message;
    }
}
