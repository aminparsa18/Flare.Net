using ClickHouse.Driver;
using Flare.Api.Model;
using Microsoft.Extensions.Options;

namespace Flare.Api.Query;

public interface IWebReplayQueryService
{
    Task<WebReplayResponse> GetChunksAsync(string sessionId, AppSessionTimelineRequest window, CancellationToken cancellationToken);

    /// <summary>One chunk's JSON array text, or null when it is gone (retention) or outside the caller's service scope.</summary>
    Task<string?> GetChunkAsync(string sessionId, Guid chunkId, long startUnixMs, CancellationToken cancellationToken);
}

/// <summary>The one component holding an <see cref="IClickHouseClient"/> for browser session replay - see <see cref="WebReplayQueryBuilder"/> for the SQL.</summary>
public sealed class WebReplayQueryService(IClickHouseClient client, IOptions<QueryLimitsOptions> queryLimits, TimeProvider timeProvider) : IWebReplayQueryService
{
    public async Task<WebReplayResponse> GetChunksAsync(string sessionId, AppSessionTimelineRequest window, CancellationToken cancellationToken)
    {
        var (from, to) = AppSessionQueryBuilder.ResolveTimelineWindow(window, timeProvider.GetUtcNow());
        var built = WebReplayQueryBuilder.BuildChunks(sessionId, from, to);
        var chunks = new List<WebReplayChunk>();
        var service = string.Empty;
        try
        {
            await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.Full(queryLimits.Value), cancellationToken);
            while (reader.Read())
            {
                if (chunks.Count == 0) service = reader.GetString(5);
                chunks.Add(new WebReplayChunk
                {
                    ChunkId = reader.GetString(0),
                    StartUnixMs = reader.GetFieldValue<long>(1),
                    EndUnixMs = reader.GetFieldValue<long>(2),
                    EventCount = (int)reader.GetFieldValue<uint>(3),
                    PayloadBytes = (long)reader.GetFieldValue<ulong>(4),
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Table missing (migration 0090 not applied yet): no replay, same page.
            chunks.Clear();
        }

        var truncated = chunks.Count > WebReplayQueryBuilder.MaxChunks;
        return new WebReplayResponse
        {
            SessionId = sessionId,
            ServiceName = service,
            Chunks = truncated ? chunks.Take(WebReplayQueryBuilder.MaxChunks).ToList() : chunks,
            Truncated = truncated,
        };
    }

    public async Task<string?> GetChunkAsync(string sessionId, Guid chunkId, long startUnixMs, CancellationToken cancellationToken)
    {
        var built = WebReplayQueryBuilder.BuildChunk(sessionId, chunkId, startUnixMs);
        await using var reader = await client.ExecuteReaderAsync(built.Sql, built.Parameters, QuerySafety.ExecutionTimeOnly(queryLimits.Value), cancellationToken);
        return reader.Read() ? reader.GetString(0) : null;
    }
}
