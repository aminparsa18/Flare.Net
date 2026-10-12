using System.Text;
using System.Text.Json;
using Flare.Api.Json;
using Flare.Api.Model;
using Flare.Identity.Audit;

namespace Flare.Api.Auditing;

public enum AuditExportFormat
{
    Csv,
    Json,
    NdJson,
}

/// <summary>
/// Serialises audit events for <c>GET /api/audit-events/export</c> (ADR-0202). Pure and
/// stream-based: the endpoint feeds it pages from the store and it never holds more than
/// one row.
/// </summary>
public static class AuditExportFormatter
{
    private static readonly string[] CsvColumns =
    [
        "id", "timestamp", "actorId", "actorName", "actorKind", "action", "resourceType", "resourceId",
        "route", "statusCode", "sourceIp", "changes", "prevHash", "hash",
    ];

    public static AuditExportFormat? Parse(string? format) => format?.ToLowerInvariant() switch
    {
        null or "" or "csv" => AuditExportFormat.Csv,
        "json" => AuditExportFormat.Json,
        "ndjson" => AuditExportFormat.NdJson,
        _ => null,
    };

    public static string ContentType(AuditExportFormat format) => format switch
    {
        AuditExportFormat.Csv => "text/csv; charset=utf-8",
        AuditExportFormat.Json => "application/json; charset=utf-8",
        _ => "application/x-ndjson; charset=utf-8",
    };

    public static string Extension(AuditExportFormat format) => format switch
    {
        AuditExportFormat.Csv => "csv",
        AuditExportFormat.Json => "json",
        _ => "ndjson",
    };

    public static AuditExportRowDto ToRow(AuditEvent e) => new()
    {
        Id = e.Id,
        Timestamp = AuditHash.TimestampText(e.Timestamp),
        ActorId = e.ActorId?.ToString(),
        ActorName = e.ActorName,
        ActorKind = e.ActorKind,
        Action = e.Action,
        ResourceType = e.ResourceType,
        ResourceId = e.ResourceId,
        Route = e.Route,
        StatusCode = e.StatusCode,
        SourceIp = e.SourceIp,
        Changes = e.Changes,
        PrevHash = e.PrevHash,
        Hash = e.Hash,
    };

    public static async Task WriteAsync(
        TextWriter writer,
        AuditExportFormat format,
        IAsyncEnumerable<AuditEvent> events,
        CancellationToken cancellationToken)
    {
        switch (format)
        {
            case AuditExportFormat.Csv:
                await writer.WriteLineAsync(string.Join(',', CsvColumns));
                await foreach (var e in events.WithCancellation(cancellationToken))
                {
                    await writer.WriteLineAsync(CsvLine(ToRow(e)));
                }

                break;

            case AuditExportFormat.NdJson:
                await foreach (var e in events.WithCancellation(cancellationToken))
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(ToRow(e), AuditJsonContext.Default.AuditExportRowDto));
                }

                break;

            default:
                await writer.WriteAsync('[');
                var first = true;
                await foreach (var e in events.WithCancellation(cancellationToken))
                {
                    await writer.WriteAsync(first ? "\n" : ",\n");
                    first = false;
                    await writer.WriteAsync(JsonSerializer.Serialize(ToRow(e), AuditJsonContext.Default.AuditExportRowDto));
                }

                await writer.WriteAsync("\n]\n");
                break;
        }
    }

    private static string CsvLine(AuditExportRowDto r)
    {
        var builder = new StringBuilder();
        string?[] cells =
        [
            r.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), r.Timestamp, r.ActorId, r.ActorName,
            r.ActorKind, r.Action, r.ResourceType, r.ResourceId, r.Route,
            r.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture), r.SourceIp, r.Changes, r.PrevHash, r.Hash,
        ];
        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(CsvCell(cells[i]));
        }

        return builder.ToString();
    }

    /// <summary>RFC 4180 quoting, plus a leading apostrophe on cells a spreadsheet would run as a
    /// formula (actor names and resource ids are user-influenced). The JSON formats are unaffected.</summary>
    internal static string CsvCell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }
}
