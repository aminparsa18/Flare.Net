using BenchmarkDotNet.Attributes;
using Flare.Benchmarks.Fixtures;

namespace Flare.Benchmarks.Component;

/// <summary>
/// Flare.Api's two response codecs (ADR-0015): System.Text.Json through each endpoint's
/// source-generated context vs MemoryPack, for every response <see cref="Scenarios">scenario</see>.
/// Component level: the codec alone, on bytes already in memory - no HTTP, no compression.
/// </summary>
[MemoryDiagnoser]
public class ApiCodecBenchmarks
{
    private Scenario _scenario = null!;
    private byte[] _json = null!;
    private byte[] _memoryPack = null!;

    [ParamsSource(nameof(ScenarioNames))]
    public string Scenario { get; set; } = string.Empty;

    public static IEnumerable<string> ScenarioNames => Scenarios.Names;

    [GlobalSetup]
    public void Setup()
    {
        _scenario = Scenarios.Get(Scenario);
        _json = _scenario.EncodeJson();
        _memoryPack = _scenario.EncodeMemoryPack();
    }

    [Benchmark]
    public byte[] Json_Encode() => _scenario.EncodeJson();

    [Benchmark]
    public byte[] MemoryPack_Encode() => _scenario.EncodeMemoryPack();

    [Benchmark]
    public object? Json_Decode() => _scenario.DecodeJson(_json);

    [Benchmark]
    public object? MemoryPack_Decode() => _scenario.DecodeMemoryPack(_memoryPack);
}
