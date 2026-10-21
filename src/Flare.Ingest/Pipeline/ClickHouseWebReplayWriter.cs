using System.Text;
using ClickHouse.Driver;

namespace Flare.Ingest.Pipeline;

/// <summary>
/// One batch of rrweb events, ready to insert into <c>web_replay_chunks</c> (<c>db/clickhouse/0090_web_replay_chunks.sql</c>).
/// <see cref="Payload"/> is the validated JSON array text as the browser sent it.
/// </summary>
public sealed record WebReplayRecord(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    string ServiceName,
    string SessionId,
    Guid ChunkId,
    int EventCount,
    byte[] Payload);

public interface IClickHouseWebReplayWriter
{
    Task WriteAsync(WebReplayRecord chunk, DateTimeOffset ingestedAt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Inserts directly, with no Redis stream in between, like screenshots (ADR-0174): a chunk is one best-effort row every few
/// seconds per recorded browser tab, and the recorder does not retry a failed upload (ADR-0264).
/// </summary>
public sealed class ClickHouseWebReplayWriter(IClickHouseClient client) : IClickHouseWebReplayWriter
{
    private static readonly IReadOnlyList<string> Columns =
        ["StartTime", "EndTime", "ServiceName", "SessionId", "ChunkId", "EventCount", "Payload", "IngestedAt"];

    public Task WriteAsync(WebReplayRecord c, DateTimeOffset ingestedAt, CancellationToken cancellationToken = default) =>
        client.InsertBinaryAsync(
            "web_replay_chunks",
            Columns,
            [[c.StartTime.UtcDateTime, c.EndTime.UtcDateTime, c.ServiceName, c.SessionId, c.ChunkId, (uint)c.EventCount, Encoding.UTF8.GetString(c.Payload), ingestedAt.UtcDateTime]],
            cancellationToken: cancellationToken);
}
