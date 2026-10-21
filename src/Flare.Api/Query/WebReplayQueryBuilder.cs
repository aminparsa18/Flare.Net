using ClickHouse.Driver.ADO.Parameters;
using ClickHouse.Driver.Utility;

namespace Flare.Api.Query;

/// <summary>
/// Pure SQL builder for browser session replay (ADR-0264), over <c>web_replay_chunks</c> (migration 0090). The window
/// is the session timeline's (<see cref="AppSessionQueryBuilder.ResolveTimelineWindow"/>); the service scope is applied to
/// every query like it is for screenshots.
/// </summary>
public static class WebReplayQueryBuilder
{
    /// <summary>Most chunks one response lists: an hour at the recorder's default 5-second flush.</summary>
    public const int MaxChunks = 720;

    /// <summary>
    /// The session's chunks in the window, oldest first, one extra row to detect truncation. Columns: ChunkId, StartUnixMs,
    /// EndUnixMs, EventCount, PayloadBytes, ServiceName. Reads no <c>Payload</c> values, only their length.
    /// </summary>
    public static AppSessionSql BuildChunks(string sessionId, DateTimeOffset from, DateTimeOffset to)
    {
        var (clauses, parameters) = Where(sessionId, from, to);
        return new AppSessionSql(
            "SELECT toString(ChunkId), toUnixTimestamp64Milli(StartTime), toUnixTimestamp64Milli(EndTime), EventCount, length(Payload), ServiceName\n" +
            $"FROM web_replay_chunks\nWHERE {string.Join(" AND ", clauses)}\nORDER BY StartTime, ChunkId\nLIMIT {MaxChunks + 1}",
            parameters);
    }

    /// <summary>One chunk's JSON text. One column: Payload. <paramref name="startUnixMs"/> is the chunk's own start, so only its partition is read.</summary>
    public static AppSessionSql BuildChunk(string sessionId, Guid chunkId, long startUnixMs)
    {
        var at = DateTimeOffset.FromUnixTimeMilliseconds(startUnixMs);
        var (clauses, parameters) = Where(sessionId, at, at.AddMilliseconds(1));
        parameters.AddParameter("chunkId", chunkId);
        clauses.Add("ChunkId = {chunkId:UUID}");
        return new AppSessionSql(
            $"SELECT Payload\nFROM web_replay_chunks\nWHERE {string.Join(" AND ", clauses)}\nLIMIT 1", parameters);
    }

    private static (List<string> Clauses, ClickHouseParameterCollection Parameters) Where(string sessionId, DateTimeOffset from, DateTimeOffset to)
    {
        var parameters = new ClickHouseParameterCollection();
        parameters.AddParameter("from", from.UtcDateTime);
        parameters.AddParameter("to", to.UtcDateTime);
        parameters.AddParameter("sessionId", sessionId);
        var clauses = new List<string>
        {
            "StartTime >= {from:DateTime64(3)}",
            "StartTime < {to:DateTime64(3)}",
            "SessionId = {sessionId:String}",
        };
        ServiceScope.Append(clauses, parameters);
        return (clauses, parameters);
    }
}
