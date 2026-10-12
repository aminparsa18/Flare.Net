using Flare.Api.Alerting;
using Flare.Api.Model;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Flare.AlertWorker.Reports;

public interface IDashboardReportMailer
{
    /// <summary>Emails <paramref name="report"/> to the schedule's recipients. Throws on any send failure.</summary>
    Task SendAsync(DashboardSchedule schedule, string dashboardName, string viewUrl, RenderedReport report, DateTimeOffset generatedAt, CancellationToken cancellationToken);
}

/// <summary>
/// Sends a rendered dashboard through the same <see cref="EmailOptions"/> SMTP server alert emails use.
/// Unlike <see cref="EmailAlertNotifier"/> it throws instead of returning a result: the report worker
/// records the exception message as the run's error.
/// </summary>
public sealed class DashboardReportMailer(IOptions<EmailOptions> options) : IDashboardReportMailer
{
    public async Task SendAsync(DashboardSchedule schedule, string dashboardName, string viewUrl, RenderedReport report, DateTimeOffset generatedAt, CancellationToken cancellationToken)
    {
        var opts = options.Value;
        if (string.IsNullOrWhiteSpace(opts.Host))
        {
            throw new InvalidOperationException("SMTP is not configured on this server (Email:Host is empty).");
        }

        var message = BuildMessage(opts.From, schedule, dashboardName, viewUrl, report, generatedAt);

        using var client = new SmtpClient();
        await client.ConnectAsync(opts.Host, opts.Port, opts.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto, cancellationToken);
        if (!string.IsNullOrWhiteSpace(opts.Username))
        {
            await client.AuthenticateAsync(opts.Username, opts.Password, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }

    internal static MimeMessage BuildMessage(string from, DashboardSchedule schedule, string dashboardName, string viewUrl, RenderedReport report, DateTimeOffset generatedAt)
    {
        var message = new MimeMessage();
        message.From.Add(MailboxAddress.Parse(from));
        foreach (var recipient in DashboardScheduleRequest.SplitRecipients(schedule.Recipients))
        {
            message.To.Add(MailboxAddress.Parse(recipient));
        }

        message.Subject = $"Flare report: {schedule.Name}";

        var generated = generatedAt.ToString("yyyy-MM-dd HH:mm 'UTC'", System.Globalization.CultureInfo.InvariantCulture);
        var text = $"{schedule.Name}\nDashboard: {dashboardName}\nGenerated: {generated}\n"
            + (viewUrl.Length > 0 ? $"\nOpen it in Flare: {viewUrl}\n" : "")
            + "\nThe dashboard is attached.\n";

        var body = new BodyBuilder { TextBody = text };
        body.Attachments.Add($"{FileName(dashboardName)}.{report.FileExtension}", report.Content, ContentType.Parse(report.ContentType));
        message.Body = body.ToMessageBody();
        return message;
    }

    /// <summary>A dashboard name made safe as a file name.</summary>
    internal static string FileName(string dashboardName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. dashboardName.Select(c => invalid.Contains(c) || char.IsWhiteSpace(c) ? '-' : c)]).Trim('-');
        return cleaned.Length == 0 ? "dashboard" : cleaned.Length > 80 ? cleaned[..80] : cleaned;
    }
}
