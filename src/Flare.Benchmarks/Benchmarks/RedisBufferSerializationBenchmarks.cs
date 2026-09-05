using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Flare.Benchmarks.TestData;
using Flare.Ingest.Model;
using Flare.Ingest.Pipeline;
using MemoryPack;

namespace Flare.Benchmarks.Benchmarks;

/// <summary>
/// MemoryPack (<see cref="RedisEventPayload"/>, ADR-0017) vs the legacy System.Text.Json
/// path (<see cref="LogEventJsonContext"/>) for the internal Redis Streams buffer payload
/// between <see cref="Flare.Ingest.Sinks.RedisStreamLogEventSink"/> and
/// <see cref="ClickHouseFlushWorker"/>. See
/// docs-internal/planning/roadmap.md's "Flare-specific JSON-vs-MemoryPack benchmark" item.
/// </summary>
/// <remarks>
/// Encode/decode benchmarks call <see cref="MemoryPackSerializer"/>/<see cref="JsonSerializer"/>
/// directly rather than through <see cref="RedisEventPayload"/>'s tag-byte wrapper - that
/// wrapper is a one-time upgrade seam (see its own remarks), and adding/stripping one byte
/// is not what either codec's cost is dominated by. The "Batch" benchmarks instead loop
/// per-item calls (one Redis Stream entry per <see cref="LogEvent"/>, same as production -
/// there is no batched wire envelope) to characterize the cost of flushing one whole
/// <c>LogEventPipelineOptions.BatchSize</c> (1,000) worth of buffered traffic.
/// </remarks>
[MemoryDiagnoser]
public class RedisBufferSerializationBenchmarks
{
    private const int BatchSize = 1_000;

    private LogEvent _typical = null!;
    private LogEvent _attributeHeavy = null!;
    private List<LogEvent> _batch = null!;

    private byte[] _typicalMemoryPack = null!;
    private byte[] _attributeHeavyMemoryPack = null!;
    private byte[] _typicalJson = null!;
    private byte[] _attributeHeavyJson = null!;
    private List<byte[]> _batchMemoryPack = null!;
    private List<byte[]> _batchJson = null!;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        _typical = LogEventFixtures.Typical(random);
        _attributeHeavy = LogEventFixtures.AttributeHeavy(random);
        _batch = LogEventFixtures.Batch(random, BatchSize);

        _typicalMemoryPack = MemoryPackSerializer.Serialize(_typical);
        _attributeHeavyMemoryPack = MemoryPackSerializer.Serialize(_attributeHeavy);
        _typicalJson = JsonSerializer.SerializeToUtf8Bytes(_typical, LogEventJsonContext.Default.LogEvent);
        _attributeHeavyJson = JsonSerializer.SerializeToUtf8Bytes(_attributeHeavy, LogEventJsonContext.Default.LogEvent);

        _batchMemoryPack = _batch.Select(e => MemoryPackSerializer.Serialize(e)).ToList();
        _batchJson = _batch.Select(e => JsonSerializer.SerializeToUtf8Bytes(e, LogEventJsonContext.Default.LogEvent)).ToList();
    }

    [Benchmark(Baseline = true)]
    public byte[] MemoryPack_Encode_Typical() => MemoryPackSerializer.Serialize(_typical);

    [Benchmark]
    public byte[] Json_Encode_Typical() => JsonSerializer.SerializeToUtf8Bytes(_typical, LogEventJsonContext.Default.LogEvent);

    [Benchmark]
    public LogEvent MemoryPack_Decode_Typical() => MemoryPackSerializer.Deserialize<LogEvent>(_typicalMemoryPack)!;

    [Benchmark]
    public LogEvent? Json_Decode_Typical() => JsonSerializer.Deserialize(_typicalJson, LogEventJsonContext.Default.LogEvent);

    [Benchmark]
    public byte[] MemoryPack_Encode_AttributeHeavy() => MemoryPackSerializer.Serialize(_attributeHeavy);

    [Benchmark]
    public byte[] Json_Encode_AttributeHeavy() => JsonSerializer.SerializeToUtf8Bytes(_attributeHeavy, LogEventJsonContext.Default.LogEvent);

    [Benchmark]
    public LogEvent MemoryPack_Decode_AttributeHeavy() => MemoryPackSerializer.Deserialize<LogEvent>(_attributeHeavyMemoryPack)!;

    [Benchmark]
    public LogEvent? Json_Decode_AttributeHeavy() => JsonSerializer.Deserialize(_attributeHeavyJson, LogEventJsonContext.Default.LogEvent);

    [Benchmark]
    public long MemoryPack_Encode_Batch()
    {
        long total = 0;
        foreach (var e in _batch)
        {
            total += MemoryPackSerializer.Serialize(e).Length;
        }

        return total;
    }

    [Benchmark]
    public long Json_Encode_Batch()
    {
        long total = 0;
        foreach (var e in _batch)
        {
            total += JsonSerializer.SerializeToUtf8Bytes(e, LogEventJsonContext.Default.LogEvent).Length;
        }

        return total;
    }

    [Benchmark]
    public int MemoryPack_Decode_Batch()
    {
        var count = 0;
        foreach (var bytes in _batchMemoryPack)
        {
            if (MemoryPackSerializer.Deserialize<LogEvent>(bytes) is not null)
            {
                count++;
            }
        }

        return count;
    }

    [Benchmark]
    public int Json_Decode_Batch()
    {
        var count = 0;
        foreach (var bytes in _batchJson)
        {
            if (JsonSerializer.Deserialize(bytes, LogEventJsonContext.Default.LogEvent) is not null)
            {
                count++;
            }
        }

        return count;
    }
}
