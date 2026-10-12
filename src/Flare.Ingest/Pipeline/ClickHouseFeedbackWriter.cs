using ClickHouse.Driver;

namespace Flare.Ingest.Pipeline;

/// <summary>One "report a problem" message, ready to insert into <c>app_feedback</c> (<c>db/clickhouse/0082_app_feedback.sql</c>).</summary>
public sealed record FeedbackRecord(
    DateTimeOffset StartTime,
    Guid FeedbackId,
    string ServiceName,
    string SessionId,
    string UserId,
    string Name,
    string Email,
    string AppVersion,
    string Message);

public interface IClickHouseFeedbackWriter
{
    Task WriteAsync(FeedbackRecord feedback, DateTimeOffset ingestedAt, CancellationToken cancellationToken = default);
}

/// <summary>Inserts directly, with no Redis stream: one small best-effort row per user message (ADR-0193), like screenshots.</summary>
public sealed class ClickHouseFeedbackWriter(IClickHouseClient client) : IClickHouseFeedbackWriter
{
    private static readonly IReadOnlyList<string> Columns =
        ["StartTime", "FeedbackId", "ServiceName", "SessionId", "UserId", "Name", "Email", "AppVersion", "Message", "IngestedAt"];

    public Task WriteAsync(FeedbackRecord f, DateTimeOffset ingestedAt, CancellationToken cancellationToken = default) =>
        client.InsertBinaryAsync(
            "app_feedback",
            Columns,
            [[f.StartTime.UtcDateTime, f.FeedbackId, f.ServiceName, f.SessionId, f.UserId, f.Name, f.Email, f.AppVersion, f.Message, ingestedAt.UtcDateTime]],
            cancellationToken: cancellationToken);
}
