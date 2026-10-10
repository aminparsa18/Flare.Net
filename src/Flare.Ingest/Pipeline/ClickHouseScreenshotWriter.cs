using ClickHouse.Driver;

namespace Flare.Ingest.Pipeline;

/// <summary>One error screenshot, ready to insert into <c>app_screenshots</c> (<c>db/clickhouse/0079_app_screenshots.sql</c>).</summary>
public sealed record ScreenshotRecord(
    DateTimeOffset StartTime,
    string ServiceName,
    string SessionId,
    string TraceId,
    string SpanId,
    string ContentType,
    byte[] Image);

public interface IClickHouseScreenshotWriter
{
    Task WriteAsync(ScreenshotRecord screenshot, DateTimeOffset ingestedAt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Inserts directly, with no Redis stream in between: a screenshot is one small, best-effort row per crash,
/// so the at-least-once buffering the telemetry pipelines pay for isn't worth the extra stream (ADR-0174).
/// </summary>
public sealed class ClickHouseScreenshotWriter(IClickHouseClient client) : IClickHouseScreenshotWriter
{
    private static readonly IReadOnlyList<string> Columns =
        ["StartTime", "ServiceName", "SessionId", "TraceId", "SpanId", "ContentType", "ImageBase64", "IngestedAt"];

    public Task WriteAsync(ScreenshotRecord s, DateTimeOffset ingestedAt, CancellationToken cancellationToken = default) =>
        client.InsertBinaryAsync(
            "app_screenshots",
            Columns,
            [[s.StartTime.UtcDateTime, s.ServiceName, s.SessionId, s.TraceId, s.SpanId, s.ContentType, Convert.ToBase64String(s.Image), ingestedAt.UtcDateTime]],
            cancellationToken: cancellationToken);
}
