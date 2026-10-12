using Flare.Api.Auditing;
using Flare.Identity.Audit;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Flare.Api.Tests.Auditing;

public class AuditForwardingTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private static AuditEvent Event(long id) =>
        new(id, T0, null, "admin", "session", "update", "alert", "r1", "PUT /x", 204, "10.0.0.1", null, "p", "h");

    private sealed class FakeStore(int count) : IAuditEventStore
    {
        public Dictionary<string, long> Cursors { get; } = [];

        public Task<IReadOnlyList<AuditEvent>> QueryAfterAsync(long afterId, int limit, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<AuditEvent>>(
                Enumerable.Range(1, count).Select(i => Event(i)).Where(e => e.Id > afterId).Take(limit).ToList());

        public Task<long> GetForwardingCursorAsync(string sink, CancellationToken ct) =>
            Task.FromResult(Cursors.GetValueOrDefault(sink));

        public Task SetForwardingCursorAsync(string sink, long lastId, CancellationToken ct)
        {
            Cursors[sink] = lastId;
            return Task.CompletedTask;
        }

        public Task AppendAsync(NewAuditEvent auditEvent, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<AuditEvent>> QueryAsync(AuditEventFilter filter, long? beforeId, int limit, CancellationToken ct) => throw new NotSupportedException();
        public Task<AuditChainVerification> VerifyChainAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<int> PruneAsync(DateTimeOffset cutoff, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class RecordingSink(int failOnCall = -1) : IAuditSink
    {
        public string Name => "rec";
        public List<long[]> Batches { get; } = [];
        private int _calls;

        public Task SendAsync(IReadOnlyList<AuditEvent> events, CancellationToken ct)
        {
            if (++_calls == failOnCall)
            {
                throw new HttpRequestException("down");
            }

            Batches.Add(events.Select(e => e.Id).ToArray());
            return Task.CompletedTask;
        }
    }

    private static AuditForwardingService Service(FakeStore store, int batchSize) =>
        new(store, [], Options.Create(new AuditOptions { Forwarding = new() { BatchSize = batchSize } }), NullLogger<AuditForwardingService>.Instance);

    [Fact]
    public async Task ForwardAsync_DeliversInOrderInBatches_AndAdvancesCursor()
    {
        var store = new FakeStore(5);
        var sink = new RecordingSink();

        var delivered = await Service(store, 2).ForwardAsync(sink, CancellationToken.None);

        Assert.Equal(5, delivered);
        Assert.Equal([[1L, 2L], [3L, 4L], [5L]], sink.Batches);
        Assert.Equal(5, store.Cursors["rec"]);
        Assert.Equal(0, await Service(store, 2).ForwardAsync(sink, CancellationToken.None));
    }

    [Fact]
    public async Task ForwardAsync_FailedBatchKeepsCursor_AndRetriesTheSameEvents()
    {
        var store = new FakeStore(4);
        var sink = new RecordingSink(failOnCall: 2);
        var service = Service(store, 2);

        var first = await service.ForwardAsync(sink, CancellationToken.None);
        Assert.Equal(2, first);
        Assert.Equal(2, store.Cursors["rec"]);

        var second = await service.ForwardAsync(sink, CancellationToken.None);
        Assert.Equal(2, second);
        Assert.Equal([[1L, 2L], [3L, 4L]], sink.Batches);
    }

    [Fact]
    public void Sign_MatchesKnownHmacSha256()
    {
        // RFC 4231 test case 2: key "Jefe", data "what do ya want for nothing?"
        var signature = WebhookAuditSink.Sign("Jefe", System.Text.Encoding.UTF8.GetBytes("what do ya want for nothing?"));

        Assert.Equal("sha256=5bdcc146bf60754e6a042426089575c75a003f089d2739839dec58b964ec3843", signature);
    }

    [Fact]
    public void SyslogFormat_IsRfc5424WithJsonMessage()
    {
        var line = AuditSyslogFormatter.Format(Event(7), "my host\n");

        Assert.StartsWith("<86>1 2026-10-01T12:00:00.000000Z myhost flare - audit - {", line);
        Assert.DoesNotContain('\n', line);
    }

    [Fact]
    public void SyslogFrame_PrefixesOctetCount()
    {
        var frame = System.Text.Encoding.UTF8.GetString(AuditSyslogFormatter.Frame("héllo"));

        Assert.Equal("6 héllo", frame);
    }
}
