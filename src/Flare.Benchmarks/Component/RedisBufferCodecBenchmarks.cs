using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Flare.Benchmarks.Fixtures.Synthetic;
using Flare.Ingest.Model;
using Flare.Ingest.Pipeline;
using MemoryPack;

namespace Flare.Benchmarks.Component;

/// <summary>
/// MemoryPack (<see cref="RedisEventPayload"/>, ADR-0017) vs the legacy System.Text.Json
/// path (<see cref="LogEventJsonContext"/>) for the internal Redis Streams buffer payload
/// between <see cref="Flare.Ingest.Sinks.RedisStreamLogEventSink"/> and
/// <see cref="ClickHouseFlushWorker"/>. Component level: one codec, one operation, one event.
/// </summary>
/// <remarks>
/// Encode/decode benchmarks call <see cref="MemoryPackSerializer"/>/<see cref="JsonSerializer"/>
/// directly rather than through <see cref="RedisEventPayload"/>'s tag-byte wrapper - that
/// wrapper is a one-time upgrade seam (see its own remarks), and adding/stripping one byte
/// is not what either codec's cost is dominated by. There is no batch case: production
/// writes one Redis Stream entry per event, so a batch is just N x <c>Typical</c>.
/// </remarks>
[MemoryDiagnoser]
public class RedisBufferCodecBenchmarks
{
    private LogEvent _typical = null!;
    private LogEvent _attributeHeavy = null!;

    private byte[] _typicalMemoryPack = null!;
    private byte[] _attributeHeavyMemoryPack = null!;
    private byte[] _typicalJson = null!;
    private byte[] _attributeHeavyJson = null!;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(42);
        _typical = LogEventFixtures.Typical(random);
        _attributeHeavy = LogEventFixtures.AttributeHeavy(random);

        _typicalMemoryPack = MemoryPackSerializer.Serialize(_typical);
        _attributeHeavyMemoryPack = MemoryPackSerializer.Serialize(_attributeHeavy);
        _typicalJson = JsonSerializer.SerializeToUtf8Bytes(_typical, LogEventJsonContext.Default.LogEvent);
        _attributeHeavyJson = JsonSerializer.SerializeToUtf8Bytes(_attributeHeavy, LogEventJsonContext.Default.LogEvent);
    }

    [Benchmark]
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
}
