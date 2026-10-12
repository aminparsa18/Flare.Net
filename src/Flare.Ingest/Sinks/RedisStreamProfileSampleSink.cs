using Flare.Ingest.Model;
using Flare.Ingest.Pipeline;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Flare.Ingest.Sinks;

/// <summary>
/// Durable buffer for the profile-sample ClickHouse insert pipeline: <c>XADD</c>s each
/// <see cref="ProfileSampleRecord"/> (MemoryPack, ADR-0017) into a Redis Stream, same
/// rationale and shape as <see cref="RedisStreamSpanEventSink"/>.
/// </summary>
public sealed class RedisStreamProfileSampleSink(
    IConnectionMultiplexer connectionMultiplexer,
    IOptions<ProfileEventPipelineOptions> options) : IProfileSampleSink
{
    private static readonly RedisValue DataField = "data";

    public async ValueTask WriteAsync(ProfileSampleRecord sample, CancellationToken cancellationToken = default)
    {
        var opts = options.Value;
        var db = connectionMultiplexer.GetDatabase();
        await db.StreamAddAsync(
            opts.StreamKey,
            DataField,
            RedisEventPayload.Encode(sample),
            maxLength: opts.StreamMaxLength,
            useApproximateMaxLength: true);
    }
}
