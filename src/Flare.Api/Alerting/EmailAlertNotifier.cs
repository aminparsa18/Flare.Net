using MailKit.Net.Smtp;
using MailKit.Security;
using Flare.Api.Model;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Flare.Api.Alerting;

/// <summary>
/// Emails a fired alert to <see cref="AlertRule.EmailTo"/> through the app-wide SMTP
/// server in <see cref="EmailOptions"/> - the Email counterpart to
/// <see cref="WebhookAlertNotifier"/>/<see cref="TelegramAlertNotifier"/>, picked by
/// <see cref="CompositeAlertNotifier"/> instead of them when a rule has
/// <see cref="AlertRule.EmailTo"/> set.
/// </summary>
/// <remarks>
/// Unlike the other two notifiers, this one owns no <see cref="System.Net.Http.HttpClient"/> -
/// MailKit's <see cref="SmtpClient"/> is its own socket-based client, connected fresh per
/// send (the standard MailKit usage pattern; volume here is low - one send per rule
/// breach past cooldown, not per request). A blank <see cref="EmailOptions.Host"/> (SMTP
/// never configured) is treated as a send failure, not a startup error - same "every
/// channel's misconfiguration becomes a recorded <c>alert_events</c> row, not a crash"
/// contract the other notifiers already follow for a bad URL/token.
///
/// <see cref="SendAsync"/> catches every non-cancellation exception, not a curated list
/// like the other two notifiers - confirmed live that MailKit/SMTP failure modes are too
/// numerous and inconsistently typed to enumerate safely (a mismatched
/// <see cref="EmailOptions.UseStartTls"/> against a server that doesn't advertise
/// STARTTLS throws a plain <see cref="NotSupportedException"/>, not one of MailKit's own
/// exception types - an earlier curated catch list here missed it, letting the failure
/// escape as an unrecorded worker-log line instead of a <c>Failed</c> <c>alert_events</c>
/// row). <see cref="SmtpCommandException"/>/<see cref="SmtpProtocolException"/>/
/// <see cref="AuthenticationException"/>/socket/TLS/timeout exceptions are all still
/// possible and all land here.
/// </remarks>
public sealed class EmailAlertNotifier(IOptions<EmailOptions> options) : IAlertNotifier
{
    public async Task<NotificationResult> SendAsync(AlertRule rule, ulong observedCount, DateTimeOffset firedAt, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.Host))
        {
            return new NotificationResult(false, 0, "SMTP is not configured on this server (Email:Host is empty).");
        }

        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(opts.From));
        foreach (var recipient in SplitRecipients(rule.EmailTo))
        {
            message.To.Add(MailboxAddress.Parse(recipient));
        }

        message.Subject = $"Flare alert: {rule.Name}";
        message.Body = new TextPart("plain") { Text = AlertMessageFormatter.BuildText(rule, observedCount) };

        try
        {
            using var client = new SmtpClient();
            await client.ConnectAsync(
                opts.Host,
                opts.Port,
                opts.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(opts.Username))
            {
                await client.AuthenticateAsync(opts.Username, opts.Password, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);
            return new NotificationResult(true, 0, null);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A client-side timeout throws OperationCanceledException too, but with the
            // *caller's* token still uncancelled - distinguishes that from real
            // cancellation-by-app-shutdown, which should propagate normally rather than
            // be recorded as a "failed" notification.
            return new NotificationResult(false, 0, ex.Message);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Bad credentials, connection refused/reset, a mismatched UseStartTls against
            // a server that doesn't support it, a rejected recipient, etc. - recorded as
            // a failed notification rather than left to bubble up and abort the tick for
            // every other rule. Real cancellation-by-app-shutdown (an OperationCanceledException
            // with the caller's token actually cancelled) is excluded by the `when` guard
            // and propagates normally instead of being swallowed here.
            return new NotificationResult(false, 0, ex.Message);
        }
    }

    /// <summary>
    /// Splits <see cref="AlertRule.EmailTo"/> on commas/semicolons, trimming whitespace
    /// and dropping empty entries - pulled out as its own pure method so it's
    /// unit-testable without real SMTP I/O.
    /// </summary>
    internal static IReadOnlyList<string> SplitRecipients(string to) =>
        to.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
