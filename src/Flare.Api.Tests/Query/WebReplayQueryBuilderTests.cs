using Flare.Api.Query;
using Xunit;

namespace Flare.Api.Tests.Query;

public class WebReplayQueryBuilderTests
{
    private static readonly DateTimeOffset End = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void BuildChunks_ListsTheSessionsChunksOldestFirstWithoutReadingPayloads()
    {
        var built = WebReplayQueryBuilder.BuildChunks("abc", End.AddHours(-1), End);

        Assert.Contains("FROM web_replay_chunks", built.Sql);
        Assert.Contains("SessionId = {sessionId:String}", built.Sql);
        Assert.Contains("StartTime >= {from:DateTime64(3)}", built.Sql);
        Assert.Contains("StartTime < {to:DateTime64(3)}", built.Sql);
        Assert.Contains("length(Payload)", built.Sql);
        Assert.DoesNotContain("SELECT Payload", built.Sql);
        Assert.Contains("ORDER BY StartTime, ChunkId\nLIMIT 721", built.Sql);
        Assert.DoesNotContain("abc", built.Sql);
    }

    [Fact]
    public void BuildChunk_ReadsOnePayloadByIdInsideTheChunksOwnMillisecond()
    {
        var chunkId = Guid.NewGuid();
        var built = WebReplayQueryBuilder.BuildChunk("abc", chunkId, 1_700_000_000_123);

        Assert.StartsWith("SELECT Payload\nFROM web_replay_chunks", built.Sql);
        Assert.Contains("ChunkId = {chunkId:UUID}", built.Sql);
        Assert.Contains("SessionId = {sessionId:String}", built.Sql);
        Assert.Contains("StartTime >= {from:DateTime64(3)}", built.Sql);
        Assert.Contains("StartTime < {to:DateTime64(3)}", built.Sql);
        Assert.EndsWith("LIMIT 1", built.Sql);
        Assert.DoesNotContain(chunkId.ToString(), built.Sql);
    }

    [Fact]
    public void BothQueriesKeepTheServiceScopeClause()
    {
        ServiceScope.Current = ["shop-*", "billing"];
        try
        {
            var chunks = WebReplayQueryBuilder.BuildChunks("abc", End.AddHours(-1), End);
            var chunk = WebReplayQueryBuilder.BuildChunk("abc", Guid.NewGuid(), 1_700_000_000_123);

            Assert.Contains("ServiceName IN {scopeExact:Array(String)}", chunks.Sql);
            Assert.Contains("startsWith(ServiceName, p)", chunks.Sql);
            Assert.Contains("ServiceName IN {scopeExact:Array(String)}", chunk.Sql);
            Assert.Contains("startsWith(ServiceName, p)", chunk.Sql);
        }
        finally
        {
            ServiceScope.Current = null;
        }
    }
}
