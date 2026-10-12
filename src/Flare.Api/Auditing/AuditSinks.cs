using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Flare.Api.Json;
using Flare.Identity.Audit;
using Microsoft.Extensions.Options;

namespace Flare.Api.Auditing;

/// <summary>One external destination for audit events (ADR-0203). Throwing means "not delivered": the forwarder retries the same batch.</summary>
public interface IAuditSink
{
    /// <summary>Stable key for the delivery cursor; changing it replays the trail to the new destination.</summary>
    string Name { get; }

    Task SendAsync(IReadOnlyList<AuditEvent> events, CancellationToken cancellationToken);
}

public sealed class WebhookAuditSink(HttpClient http, string url, string? secret) : IAuditSink
{
    public string Name => "webhook";

    public async Task SendAsync(IReadOnlyList<AuditEvent> events, CancellationToken cancellationToken)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(
            events.Select(AuditExportFormatter.ToRow).ToList(), AuditJsonContext.Default.ListAuditExportRowDto);
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new("application/json");
        if (!string.IsNullOrEmpty(secret))
        {
            request.Headers.Add("X-Flare-Signature", Sign(secret, body));
        }

        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public static string Sign(string secret, byte[] body) =>
        "sha256=" + Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));
}

public sealed class SyslogAuditSink(string host, int port, bool udp) : IAuditSink
{
    public string Name => "syslog";

    public async Task SendAsync(IReadOnlyList<AuditEvent> events, CancellationToken cancellationToken)
    {
        var hostname = Environment.MachineName;
        if (udp)
        {
            using var client = new UdpClient();
            client.Connect(host, port);
            foreach (var e in events)
            {
                await client.SendAsync(Encoding.UTF8.GetBytes(AuditSyslogFormatter.Format(e, hostname)), cancellationToken);
            }

            return;
        }

        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, cancellationToken);
        var stream = tcp.GetStream();
        foreach (var e in events)
        {
            await stream.WriteAsync(AuditSyslogFormatter.Frame(AuditSyslogFormatter.Format(e, hostname)), cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }
}

public static class AuditSyslogFormatter
{
    // authpriv (10) * 8 + informational (6).
    private const int Priority = 86;

    /// <summary>RFC 5424 line; the message is the export row as JSON, so a collector can parse and re-verify the hash.</summary>
    public static string Format(AuditEvent e, string hostname) =>
        $"<{Priority}>1 {e.Timestamp.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.ffffffZ} {Token(hostname)} flare - audit - "
        + JsonSerializer.Serialize(AuditExportFormatter.ToRow(e), AuditJsonContext.Default.AuditExportRowDto);

    /// <summary>RFC 6587 octet-counting frame: "&lt;byte length&gt; &lt;message&gt;".</summary>
    public static byte[] Frame(string message)
    {
        var bytes = Encoding.UTF8.GetBytes(message);
        return [.. Encoding.ASCII.GetBytes(bytes.Length + " "), .. bytes];
    }

    // Header fields are space-delimited printable ASCII; nothing else may reach them.
    private static string Token(string value)
    {
        var cleaned = new string(value.Where(c => c is > ' ' and < '\u007f').ToArray());
        return cleaned.Length == 0 ? "-" : cleaned;
    }
}
