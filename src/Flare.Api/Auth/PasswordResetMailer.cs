using Flare.Api.Alerting;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Flare.Api.Auth;

/// <summary>Emails a set-password link for the self-service "forgot password" flow (ADR-0113).</summary>
public interface IPasswordResetMailer
{
    /// <summary>True when SMTP (<c>Email:Host</c>/<c>Email:From</c>) and the dashboard's public URL
    /// (<c>Alerting:PublicUrl</c>) are both configured - without either, no usable link can be sent.</summary>
    bool IsConfigured { get; }

    /// <summary>Builds <c>{PublicUrl}/set-password?token=...</c>.</summary>
    string BuildLink(string rawToken);

    /// <summary>Sends the link; returns false (never throws, except on cancellation) when delivery fails.</summary>
    Task<bool> SendAsync(string to, string link, CancellationToken cancellationToken, bool invite = false);
}

/// <summary>MailKit-backed <see cref="IPasswordResetMailer"/> over the same app-wide SMTP server
/// <see cref="EmailAlertNotifier"/> uses. Also owns the per-address cooldown that keeps the
/// unauthenticated endpoint from being used to mail-bomb someone.</summary>
public sealed class PasswordResetMailer(IOptions<EmailOptions> email, IOptions<AlertLinkOptions> links, ILogger<PasswordResetMailer> logger) : IPasswordResetMailer
{
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(email.Value.Host)
        && !string.IsNullOrWhiteSpace(email.Value.From)
        && !string.IsNullOrWhiteSpace(links.Value.PublicUrl);

    public string BuildLink(string rawToken) =>
        $"{links.Value.PublicUrl.TrimEnd('/')}/set-password?token={Uri.EscapeDataString(rawToken)}";

    public async Task<bool> SendAsync(string to, string link, CancellationToken cancellationToken, bool invite = false)
    {
        var opts = email.Value;
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(opts.From));
        message.To.Add(MailboxAddress.Parse(to));
        message.Subject = invite ? "You're invited to Flare" : "Reset your Flare password";
        message.Body = new TextPart("plain")
        {
            Text = invite
                ? $"You've been invited to Flare.\n\nSet your password (the link works once and expires in 3 days):\n{link}\n"
                : $"Someone asked to reset the password for this Flare account.\n\nSet a new password (the link works once and expires in 1 hour):\n{link}\n\nIf this wasn't you, ignore this email - your password is unchanged.\n",
        };

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(opts.Host, opts.Port, opts.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, cancellationToken);
            if (!string.IsNullOrWhiteSpace(opts.Username))
            {
                await client.AuthenticateAsync(opts.Username, opts.Password, cancellationToken);
            }
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Same reasoning as EmailAlertNotifier: SMTP failure modes are too varied to enumerate.
            logger.LogWarning(ex, "Failed to send password reset email");
            return false;
        }
    }
}
