using Flare.Ingest.Model;
using Flare.Ingest.Pipeline;

namespace Flare.Ingest.Tests.Pipeline;

/// <summary>
/// Hand-written <see cref="IClickHouseLogEventWriter"/> test double: records every batch
/// passed to it, and can be told to throw on the next call (to exercise
/// <see cref="ClickHouseFlushWorker"/>'s "leave un-acked on failure" path). The repo has
/// no mocking framework, so this follows the same plain-fake style as the rest of
/// <c>Flare.Ingest.Tests</c> rather than adding one.
/// </summary>
public sealed class FakeClickHouseLogEventWriter : IClickHouseLogEventWriter
{
    private readonly List<IReadOnlyList<LogEvent>> writtenBatches = [];

    public IReadOnlyList<IReadOnlyList<LogEvent>> WrittenBatches => writtenBatches;

    /// <summary>When set, <see cref="WriteBatchAsync"/> throws this instead of recording the batch.</summary>
    public Exception? ThrowOnNextWrite { get; set; }

    public Task WriteBatchAsync(IReadOnlyList<LogEvent> events, CancellationToken cancellationToken = default)
    {
        if (ThrowOnNextWrite is { } exception)
        {
            ThrowOnNextWrite = null;
            throw exception;
        }

        writtenBatches.Add(events);
        return Task.CompletedTask;
    }
}
