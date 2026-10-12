using Flare.Ingest.Model;

namespace Flare.Ingest.Sinks;

/// <summary>
/// The seam between the OTLP profiles receiver and whatever eventually persists profile
/// samples. Implemented by <see cref="RedisStreamProfileSampleSink"/>; a parallel,
/// deliberately un-unified pipeline like <see cref="ISpanEventSink"/> (see
/// <see cref="Pipeline.ProfileFlushWorker"/>).
/// </summary>
public interface IProfileSampleSink
{
    ValueTask WriteAsync(ProfileSampleRecord sample, CancellationToken cancellationToken = default);
}
