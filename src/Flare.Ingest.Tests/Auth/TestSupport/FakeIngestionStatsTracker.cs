using Flare.Ingest.Stats;

namespace Flare.Ingest.Tests.Auth.TestSupport;

/// <summary>Captures rejections only - the middleware never records accepted stats.</summary>
internal sealed class FakeIngestionStatsTracker : IIngestionStatsTracker
{
    public List<(IngestionSignal Signal, IngestionProtocol Protocol, string Reason)> Rejected { get; } = [];

    public ValueTask RecordAcceptedAsync(IngestionSignal signal, IngestionProtocol protocol, int recordCount, long byteCount, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;

    public ValueTask RecordRejectedAsync(IngestionSignal signal, IngestionProtocol protocol, string reason, CancellationToken cancellationToken = default)
    {
        Rejected.Add((signal, protocol, reason));
        return ValueTask.CompletedTask;
    }

    public ValueTask RecordServiceBreakdownAsync(IngestionSignal signal, IReadOnlyDictionary<string, ServiceAcceptedCounts> perService, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
