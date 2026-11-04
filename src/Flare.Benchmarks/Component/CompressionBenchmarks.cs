using BenchmarkDotNet.Attributes;
using Flare.Benchmarks.Fixtures;

namespace Flare.Benchmarks.Component;

/// <summary>
/// Server-side compression cost of an already-encoded response body, per codec and level.
/// Compressed sizes (and so the ratio) come from <see cref="Export.FixtureExporter"/>'s
/// manifest, not from this class; client-side decompression is measured in Node
/// (<c>src/dashboard/bench/component/decompress.bench.ts</c>).
/// </summary>
[MemoryDiagnoser]
public class CompressionBenchmarks
{
    private byte[] _body = null!;

    [ParamsSource(nameof(ScenarioNames))]
    public string Scenario { get; set; } = string.Empty;

    [Params("json", "memorypack")]
    public string Format { get; set; } = string.Empty;

    [ParamsSource(nameof(Codecs))]
    public string Codec { get; set; } = string.Empty;

    public static IEnumerable<string> ScenarioNames => Scenarios.Names;

    public static IEnumerable<string> Codecs => Compression.Compressing;

    [GlobalSetup]
    public void Setup()
    {
        var scenario = Scenarios.Get(Scenario);
        _body = Format == "json" ? scenario.EncodeJson() : scenario.EncodeMemoryPack();
    }

    [Benchmark]
    public int Compress() => Compression.Compress(_body, Codec).Length;
}
