using Flare.Ingest.Auth;

namespace Flare.Ingest.Tests.Auth.TestSupport;

/// <summary>In-memory <see cref="IIngestKeyUsageStore"/> - returns a fixed
/// <see cref="Usage"/> per key and captures every <see cref="RecordAsync"/> call.</summary>
internal sealed class FakeIngestKeyUsageStore : IIngestKeyUsageStore
{
    public Dictionary<Guid, IngestKeyUsage> Usage { get; } = [];

    public List<(Guid KeyId, long Events, long Bytes)> Recorded { get; } = [];

    public int GetCalls { get; private set; }

    public bool ThrowOnGet { get; set; }

    public ValueTask<IngestKeyUsage> GetAsync(Guid keyId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        GetCalls++;
        if (ThrowOnGet)
        {
            throw new InvalidOperationException("Redis is down");
        }
        return ValueTask.FromResult(Usage.GetValueOrDefault(keyId));
    }

    public ValueTask RecordAsync(Guid keyId, long events, long bytes, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        Recorded.Add((keyId, events, bytes));
        return ValueTask.CompletedTask;
    }
}
