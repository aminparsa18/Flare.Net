using BenchmarkDotNet.Attributes;
using Flare.Benchmarks.Fixtures;

namespace Flare.Benchmarks.Component;

/// <summary>
/// The request direction of ADR-0015: Flare.Api decoding a <c>POST /api/logs/search</c> body sent
/// as JSON vs MemoryPack (<see cref="Scenarios.LogSearchRequest"/>). The dashboard-side encode of
/// the same body is in <c>src/dashboard/bench/component/request-encode.bench.ts</c>.
/// </summary>
[MemoryDiagnoser]
public class RequestCodecBenchmarks
{
    private byte[] _json = null!;
    private byte[] _memoryPack = null!;

    [GlobalSetup]
    public void Setup()
    {
        _json = Scenarios.LogSearchRequest.EncodeJson();
        _memoryPack = Scenarios.LogSearchRequest.EncodeMemoryPack();
    }

    [Benchmark]
    public object? Json_Decode() => Scenarios.LogSearchRequest.DecodeJson(_json);

    [Benchmark]
    public object? MemoryPack_Decode() => Scenarios.LogSearchRequest.DecodeMemoryPack(_memoryPack);
}
